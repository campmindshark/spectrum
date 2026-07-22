using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;

namespace Spectrum {

  // A triggered electrical accent routed over the dome's actual strut graph.
  // Each strike has one connected pole-to-pole main bolt plus short branches
  // that leave (but never reuse) the main route. The bolt is progressively
  // revealed during its live duration; the persistent layer buffer then holds
  // the fading electrical afterimage for the configured half-life.
  class LEDDomeArcLightningVisualizer : DomeLayerVisualizer {

    private sealed class ActiveStrike {
      public ArcLightningPath Path;
      public double Age;
      public int PaletteIndex;
    }

    private const int MaxActiveStrikes = 8;

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private readonly IAudioLevelInput audio;
    private readonly OrientationInput orientationInput;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly LayerTrigger trigger;
    private readonly ArcLightningGraph graph;
    private readonly int[][] strutPixels;
    private readonly Vector2[] vertexPositions;
    private readonly Random random = new Random();
    private readonly Stopwatch frameTimer = new Stopwatch();
    private readonly List<ActiveStrike> strikes = new List<ActiveStrike>();
    private int paletteCursor;

    public LEDDomeArcLightningVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      IAudioLevelInput audio,
      OrientationInput orientationInput,
      BeatBroadcaster beats,
      DomeRenderContext dome
    ) {
      this.environment = environment;
      this.runtime = runtime;
      this.audio = audio;
      this.orientationInput = orientationInput;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.trigger = new LayerTrigger(
        environment, orientationInput, runtime.InstanceId, beats, audio);
      this.graph = new ArcLightningGraph(StrutLayoutFactory.lines);
      this.strutPixels = BuildStrutPixels(
        this.buffer.Topology, StrutLayoutFactory.lines.GetLength(0));
      this.vertexPositions = BuildVertexPositions(this.graph);
    }

    public int Priority => 2;
    public string LayerKey => "arc-lightning";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() =>
      this.inputs ?? (this.inputs = new Input[] {
        this.audio, this.orientationInput,
      });

    public void Visualize() {
      ArcLightningLayerOptions options =
        this.runtime.GetOptions<ArcLightningLayerOptions>();
      double elapsed = this.ElapsedSeconds();

      // Afterglow is expressed as a brightness half-life. Zero is an explicit
      // no-trail mode and clears all pixels before the current live bolt paints.
      double retention = AfterglowRetention(options.Afterglow, elapsed);
      this.buffer.Fade(retention, 0);

      // Always poll both generations so neither trigger baseline goes stale.
      bool fired = this.trigger.Fired(
        options.Button, options.Trigger, options.Level, options.Interval);
      bool cleared = this.trigger.Cleared();
      if (cleared) {
        this.strikes.Clear();
        this.ClearBuffer();
      }
      if (fired) {
        this.CreateStrikes(options);
      }

      this.AdvanceStrikes(elapsed, options.Duration);
      this.PaintStrikes(options);
    }

    private double ElapsedSeconds() {
      if (!this.frameTimer.IsRunning) {
        this.frameTimer.Restart();
        return 0;
      }
      double elapsed = this.frameTimer.Elapsed.TotalSeconds;
      this.frameTimer.Restart();
      // A debugger pause should not instantly retire every live strike.
      return Math.Min(elapsed, 0.1);
    }

    private void CreateStrikes(ArcLightningLayerOptions options) {
      List<ArcLightningPole> poles = this.CollectPoles();
      ImmutableArray<ArcLightningPolePair> pairs = BuildPoleFan(
        poles, MaxActiveStrikes);
      if (pairs.Length == 0) {
        pairs = ImmutableArray.Create(FallbackPolePair(
          this.graph, poles, this.random));
      }

      foreach (ArcLightningPolePair pair in pairs) {
        int destination = pair.Destination == pair.Origin
          ? this.graph.FarthestVertex(pair.Origin)
          : pair.Destination;
        ArcLightningPath path = this.graph.Route(
          pair.Origin, destination, options.BranchCount,
          options.Jaggedness, this.random);
        if (this.strikes.Count >= MaxActiveStrikes) {
          this.strikes.RemoveAt(0);
        }
        this.strikes.Add(new ActiveStrike {
          Path = path,
          Age = 0,
          PaletteIndex = this.paletteCursor++ & 7,
        });
      }
    }

    // Every eligible moving wand becomes a pole. The lowest device id is the
    // stable primary positive pole; one strike fans from it to each remaining
    // negative pole. One/no-wand fallbacks are created by CreateStrikes after
    // this method returns fewer than two poles.
    private List<ArcLightningPole> CollectPoles() {
      var poles = new List<ArcLightningPole>();
      IReadOnlyDictionary<int, OrientationDevice> devices =
        this.orientationInput.OperatorFrameDevices;
      int spotlight = this.environment.SpotlightDeviceId;
      bool spotlightMoving = spotlight >= 0
        && devices.TryGetValue(spotlight, out OrientationDevice spotlightDevice)
        && spotlightDevice.isMoving;

      if (spotlight != -2) {
        foreach (var kvp in devices) {
          if (!kvp.Value.isMoving ||
              (spotlightMoving && kvp.Key != spotlight)) {
            continue;
          }
          Vector3 aim = Vector3.Transform(
            OrientationCenter.Spot,
            Quaternion.Conjugate(kvp.Value.currentRotation()));
          Vector2 stripAim = StrutLayoutFactory.ProjectSphereToStrip(
            aim, foldAxisToUpperHemisphere: true);
          poles.Add(new ArcLightningPole(
            kvp.Key, this.NearestVertex(stripAim)));
        }
      }
      poles.Sort((a, b) => a.DeviceId.CompareTo(b.DeviceId));
      return poles;
    }

    internal static ImmutableArray<ArcLightningPolePair> BuildPoleFan(
      IEnumerable<ArcLightningPole> poles, int maxPairs
    ) {
      if (poles == null || maxPairs <= 0) {
        return ImmutableArray<ArcLightningPolePair>.Empty;
      }
      var ordered = new List<ArcLightningPole>(poles);
      ordered.Sort((a, b) => a.DeviceId.CompareTo(b.DeviceId));
      if (ordered.Count < 2) {
        return ImmutableArray<ArcLightningPolePair>.Empty;
      }
      int count = Math.Min(maxPairs, ordered.Count - 1);
      var pairs = ImmutableArray.CreateBuilder<ArcLightningPolePair>(count);
      for (int i = 1; i <= count; i++) {
        pairs.Add(new ArcLightningPolePair(
          ordered[0].Vertex, ordered[i].Vertex));
      }
      return pairs.MoveToImmutable();
    }

    internal static ArcLightningPolePair FallbackPolePair(
      ArcLightningGraph graph,
      IReadOnlyList<ArcLightningPole> poles,
      Random random
    ) {
      if (graph == null) {
        throw new ArgumentNullException(nameof(graph));
      }
      random ??= new Random(0);
      int origin = poles != null && poles.Count == 1
        ? poles[0].Vertex
        : random.Next(graph.VertexCount);
      return new ArcLightningPolePair(origin, graph.FarthestVertex(origin));
    }

    private int NearestVertex(Vector2 stripPoint) {
      Vector2 normalized = new Vector2(
        (stripPoint.X + 1) * 0.5f,
        (stripPoint.Y + 1) * 0.5f);
      int nearest = 0;
      double bestDistance = double.MaxValue;
      for (int i = 0; i < this.vertexPositions.Length; i++) {
        double distance = Vector2.DistanceSquared(
          normalized, this.vertexPositions[i]);
        if (distance < bestDistance) {
          bestDistance = distance;
          nearest = i;
        }
      }
      return nearest;
    }

    private void AdvanceStrikes(double elapsed, double duration) {
      for (int i = this.strikes.Count - 1; i >= 0; i--) {
        this.strikes[i].Age += elapsed;
        if (this.strikes[i].Age > duration) {
          this.strikes.RemoveAt(i);
        }
      }
    }

    private void PaintStrikes(ArcLightningLayerOptions options) {
      for (int i = 0; i < this.strikes.Count; i++) {
        ActiveStrike strike = this.strikes[i];
        double life = Math.Clamp(strike.Age / options.Duration, 0, 1);
        double flicker = 0.9 + 0.1 * Math.Sin(
          (strike.PaletteIndex + 1) * 19 + life * 83);
        int mainVisible = VisibleEdgeCount(
          strike.Path.MainStruts.Length, strike.Age, options.Duration);
        this.PaintSequence(
          strike.Path.MainStruts, mainVisible, flicker,
          options.Width, strike.PaletteIndex, options.Palette);

        for (int branch = 0;
             branch < strike.Path.BranchStruts.Length;
             branch++) {
          ImmutableArray<int> edges = strike.Path.BranchStruts[branch];
          int visible = VisibleEdgeCount(
            edges.Length, strike.Age, options.Duration);
          this.PaintSequence(
            edges, visible, flicker * 0.62,
            options.Width, strike.PaletteIndex + branch + 1,
            options.Palette);
        }
      }
    }

    private void PaintSequence(
      ImmutableArray<int> struts, int visibleCount,
      double brightness, int width, int paletteIndex, int selectedPalette
    ) {
      int count = Math.Min(visibleCount, struts.Length);
      for (int edge = 0; edge < count; edge++) {
        int strut = struts[edge];
        if (strut < 0 || strut >= this.strutPixels.Length) {
          continue;
        }
        foreach (int pixel in this.strutPixels[strut]) {
          this.PaintPixel(pixel, brightness, paletteIndex, selectedPalette);
          for (int radius = 0;
               radius < width - 1 && radius < DomeFrame.NeighborRadii;
               radius++) {
            double halo = brightness * Math.Pow(0.42, radius + 1);
            for (int direction = 0;
                 direction < DomeFrame.NeighborDirections;
                 direction += 2) {
              this.PaintPixel(
                this.buffer.NeighborAt(pixel, direction, radius),
                halo, paletteIndex, selectedPalette);
            }
          }
        }
      }
    }

    private void PaintPixel(
      int pixelIndex, double brightness, int paletteIndex, int selectedPalette
    ) {
      brightness = Math.Clamp(brightness, 0, 1);
      int color = this.dome.GetGradientColor(
        paletteIndex & 7, brightness, 0, true, selectedPalette);
      color = ScaleColor(color, brightness);
      ref LEDDomeOutputPixel pixel = ref this.buffer.pixels[pixelIndex];
      int oldEnergy = ((pixel.color >> 16) & 0xFF)
        + ((pixel.color >> 8) & 0xFF) + (pixel.color & 0xFF);
      int newEnergy = ((color >> 16) & 0xFF)
        + ((color >> 8) & 0xFF) + (color & 0xFF);
      if (newEnergy >= oldEnergy) {
        pixel.color = color;
        pixel.hue = (paletteIndex & 7) / 8.0;
      }
    }

    private void ClearBuffer() {
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        this.buffer.pixels[i].Clear();
      }
    }

    internal static int VisibleEdgeCount(
      int edgeCount, double age, double duration
    ) {
      if (edgeCount <= 0) {
        return 0;
      }
      double progress = duration <= 0 ? 1 : Math.Clamp(age / duration, 0, 1);
      // Show the origin immediately, then race the leading edge over the graph.
      return Math.Clamp(
        (int)Math.Ceiling(edgeCount * (0.12 + 0.88 * progress)),
        1, edgeCount);
    }

    internal static double AfterglowRetention(
      double halfLife, double elapsed
    ) => halfLife <= 0
      ? 0 : Math.Pow(0.5, Math.Max(0, elapsed) / halfLife);

    private static int ScaleColor(int color, double brightness) {
      brightness = Math.Clamp(brightness, 0, 1);
      int r = (int)(((color >> 16) & 0xFF) * brightness);
      int g = (int)(((color >> 8) & 0xFF) * brightness);
      int b = (int)((color & 0xFF) * brightness);
      return (r << 16) | (g << 8) | b;
    }

    private static int[][] BuildStrutPixels(
      DomeTopology topology, int strutCount
    ) {
      var lists = new List<int>[strutCount];
      for (int i = 0; i < lists.Length; i++) {
        lists[i] = new List<int>();
      }
      for (int pixel = 0; pixel < topology.PixelCount; pixel++) {
        int strut = topology.PixelAt(pixel).StrutIndex;
        if (strut >= 0 && strut < lists.Length) {
          lists[strut].Add(pixel);
        }
      }
      var result = new int[strutCount][];
      for (int i = 0; i < result.Length; i++) {
        result[i] = lists[i].ToArray();
      }
      return result;
    }

    private static Vector2[] BuildVertexPositions(ArcLightningGraph graph) {
      var positions = new Vector2[graph.VertexCount];
      var assigned = new bool[graph.VertexCount];
      foreach (ArcLightningEdge edge in graph.Edges) {
        if (!assigned[edge.A]) {
          Tuple<double, double> point =
            StrutLayoutFactory.GetProjectedPoint(edge.Strut, 0);
          positions[edge.A] = new Vector2(
            (float)point.Item1, (float)point.Item2);
          assigned[edge.A] = true;
        }
        if (!assigned[edge.B]) {
          Tuple<double, double> point =
            StrutLayoutFactory.GetProjectedPoint(edge.Strut, 1);
          positions[edge.B] = new Vector2(
            (float)point.Item1, (float)point.Item2);
          assigned[edge.B] = true;
        }
      }
      return positions;
    }
  }

  internal readonly record struct ArcLightningPole(int DeviceId, int Vertex);

  internal readonly record struct ArcLightningPolePair(
    int Origin, int Destination);

  internal readonly record struct ArcLightningEdge(int A, int B, int Strut);

  internal sealed class ArcLightningPath {
    public ImmutableArray<int> MainVertices { get; }
    public ImmutableArray<int> MainStruts { get; }
    public ImmutableArray<ImmutableArray<int>> BranchStruts { get; }

    public ArcLightningPath(
      ImmutableArray<int> mainVertices,
      ImmutableArray<int> mainStruts,
      ImmutableArray<ImmutableArray<int>> branchStruts
    ) {
      this.MainVertices = mainVertices;
      this.MainStruts = mainStruts;
      this.BranchStruts = branchStruts;
    }
  }

  // Testable graph core: a randomized positive weighting keeps Dijkstra's route
  // connected and bounded while jaggedness can select longer alternatives.
  internal sealed class ArcLightningGraph {

    private readonly ImmutableArray<ArcLightningEdge> edges;
    private readonly List<int>[] adjacency;

    public int VertexCount => this.adjacency.Length;
    public ImmutableArray<ArcLightningEdge> Edges => this.edges;

    public ArcLightningGraph(int[,] lines) {
      if (lines == null) {
        throw new ArgumentNullException(nameof(lines));
      }
      if (lines.GetLength(1) != 2 || lines.GetLength(0) == 0) {
        throw new ArgumentException(
          "Lightning graph edges must have two vertices.", nameof(lines));
      }

      int maxVertex = -1;
      var edgeBuilder = ImmutableArray.CreateBuilder<ArcLightningEdge>(
        lines.GetLength(0));
      for (int edge = 0; edge < lines.GetLength(0); edge++) {
        int a = lines[edge, 0];
        int b = lines[edge, 1];
        if (a < 0 || b < 0 || a == b) {
          throw new ArgumentException(
            "Lightning graph contains an invalid edge.", nameof(lines));
        }
        maxVertex = Math.Max(maxVertex, Math.Max(a, b));
        edgeBuilder.Add(new ArcLightningEdge(a, b, edge));
      }
      this.edges = edgeBuilder.MoveToImmutable();
      this.adjacency = new List<int>[maxVertex + 1];
      for (int vertex = 0; vertex < this.adjacency.Length; vertex++) {
        this.adjacency[vertex] = new List<int>();
      }
      for (int edge = 0; edge < this.edges.Length; edge++) {
        this.adjacency[this.edges[edge].A].Add(edge);
        this.adjacency[this.edges[edge].B].Add(edge);
      }
    }

    public ArcLightningPath Route(
      int origin, int destination, int branchCount,
      double jaggedness, Random random
    ) {
      this.ValidateVertex(origin, nameof(origin));
      this.ValidateVertex(destination, nameof(destination));
      if (origin == destination) {
        throw new ArgumentException(
          "Lightning poles must be distinct.", nameof(destination));
      }
      random ??= new Random(0);
      jaggedness = Math.Clamp(jaggedness, 0, 1);

      var edgeWeights = new double[this.edges.Length];
      for (int edge = 0; edge < edgeWeights.Length; edge++) {
        double variation = random.NextDouble() * 2 - 0.75;
        edgeWeights[edge] = Math.Max(
          0.1, 1 + jaggedness * variation);
      }

      this.ShortestRoute(
        origin, destination, edgeWeights,
        out ImmutableArray<int> mainVertices,
        out ImmutableArray<int> mainStruts);
      ImmutableArray<ImmutableArray<int>> branches = this.BuildBranches(
        mainVertices, mainStruts, Math.Max(0, branchCount), random);
      return new ArcLightningPath(mainVertices, mainStruts, branches);
    }

    public int FarthestVertex(int origin) {
      this.ValidateVertex(origin, nameof(origin));
      var distance = new int[this.VertexCount];
      Array.Fill(distance, -1);
      var queue = new Queue<int>();
      distance[origin] = 0;
      queue.Enqueue(origin);
      int farthest = origin;
      while (queue.Count > 0) {
        int vertex = queue.Dequeue();
        if (distance[vertex] > distance[farthest]) {
          farthest = vertex;
        }
        foreach (int edgeIndex in this.adjacency[vertex]) {
          int next = Other(this.edges[edgeIndex], vertex);
          if (distance[next] != -1) {
            continue;
          }
          distance[next] = distance[vertex] + 1;
          queue.Enqueue(next);
        }
      }
      return farthest;
    }

    private void ShortestRoute(
      int origin, int destination, double[] edgeWeights,
      out ImmutableArray<int> vertices,
      out ImmutableArray<int> struts
    ) {
      var distance = new double[this.VertexCount];
      Array.Fill(distance, double.PositiveInfinity);
      var previousEdge = new int[this.VertexCount];
      Array.Fill(previousEdge, -1);
      var queue = new PriorityQueue<int, double>();
      distance[origin] = 0;
      queue.Enqueue(origin, 0);

      while (queue.TryDequeue(out int vertex, out double queuedDistance)) {
        if (queuedDistance > distance[vertex]) {
          continue;
        }
        if (vertex == destination) {
          break;
        }
        foreach (int edgeIndex in this.adjacency[vertex]) {
          int next = Other(this.edges[edgeIndex], vertex);
          double candidate = distance[vertex] + edgeWeights[edgeIndex];
          if (candidate >= distance[next]) {
            continue;
          }
          distance[next] = candidate;
          previousEdge[next] = edgeIndex;
          queue.Enqueue(next, candidate);
        }
      }
      if (previousEdge[destination] == -1) {
        throw new InvalidOperationException(
          "Lightning graph has no route between the selected poles.");
      }

      var reversedVertices = new List<int> { destination };
      var reversedStruts = new List<int>();
      int current = destination;
      while (current != origin) {
        int edgeIndex = previousEdge[current];
        ArcLightningEdge edge = this.edges[edgeIndex];
        reversedStruts.Add(edge.Strut);
        current = Other(edge, current);
        reversedVertices.Add(current);
      }
      reversedVertices.Reverse();
      reversedStruts.Reverse();
      vertices = ImmutableArray.CreateRange(reversedVertices);
      struts = ImmutableArray.CreateRange(reversedStruts);
    }

    private ImmutableArray<ImmutableArray<int>> BuildBranches(
      ImmutableArray<int> mainVertices,
      ImmutableArray<int> mainStruts,
      int branchCount,
      Random random
    ) {
      var branches = ImmutableArray.CreateBuilder<ImmutableArray<int>>();
      var usedStruts = new HashSet<int>(mainStruts);
      var mainVertexSet = new HashSet<int>(mainVertices);
      int attempts = 0;
      int maxAttempts = branchCount * 12 + 24;
      while (branches.Count < branchCount && attempts++ < maxAttempts) {
        int anchor = mainVertices[random.Next(mainVertices.Length)];
        var branch = ImmutableArray.CreateBuilder<int>();
        int current = anchor;
        int length = 1 + random.Next(3);
        for (int step = 0; step < length; step++) {
          var candidates = new List<int>();
          foreach (int edgeIndex in this.adjacency[current]) {
            ArcLightningEdge edge = this.edges[edgeIndex];
            int next = Other(edge, current);
            if (!usedStruts.Contains(edge.Strut)
                && (step > 0 || !mainVertexSet.Contains(next))) {
              candidates.Add(edgeIndex);
            }
          }
          if (candidates.Count == 0) {
            break;
          }
          int selected = candidates[random.Next(candidates.Count)];
          ArcLightningEdge selectedEdge = this.edges[selected];
          usedStruts.Add(selectedEdge.Strut);
          branch.Add(selectedEdge.Strut);
          current = Other(selectedEdge, current);
        }
        if (branch.Count > 0) {
          branches.Add(branch.ToImmutable());
        }
      }
      return branches.ToImmutable();
    }

    private static int Other(ArcLightningEdge edge, int vertex) =>
      edge.A == vertex ? edge.B : edge.A;

    private void ValidateVertex(int vertex, string parameterName) {
      if (vertex < 0 || vertex >= this.VertexCount) {
        throw new ArgumentOutOfRangeException(parameterName);
      }
    }
  }
}

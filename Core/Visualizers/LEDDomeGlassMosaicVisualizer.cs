using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace Spectrum {

  // A persistent stained-glass state over the dome's actual triangular faces.
  // Triggered waves rotate tile palette phases across shared-edge adjacency.
  // An optional transition makes each arriving tile narrow its old phase to
  // edge-on before opening the new phase across those same physical borders.
  // The installation has LEDs on face borders rather than inside faces, so a
  // shared strut interpolates the states of its one or two incident tiles and
  // brightens while either tile is receiving the cascade.
  class LEDDomeGlassMosaicVisualizer : DomeLayerVisualizer {

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private readonly IAudioLevelInput audio;
    private readonly OrientationInput orientationInput;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly LayerTrigger trigger;
    private readonly GlassMosaicTopology topology;
    private readonly GlassMosaicState state;
    private readonly int[][] strutPixels;
    private readonly Stopwatch frameTimer = new Stopwatch();
    private int fallbackTile;

    public LEDDomeGlassMosaicVisualizer(
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
      this.topology = new GlassMosaicTopology(
        StrutLayoutFactory.lines,
        BuildVertexPositions(StrutLayoutFactory.lines));
      this.state = new GlassMosaicState(this.topology, 0x474C4153);
      this.strutPixels = BuildStrutPixels(
        this.buffer.Topology, StrutLayoutFactory.lines.GetLength(0));
      this.fallbackTile = this.topology.NearestTile(
        new Vector2(0.5f, 0.5f));
    }

    public int Priority => 2;
    public string LayerKey => "glass-mosaic";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() =>
      this.inputs ?? (this.inputs = new Input[] {
        this.audio, this.orientationInput,
      });

    public void Visualize() {
      GlassMosaicLayerOptions options =
        this.runtime.GetOptions<GlassMosaicLayerOptions>();
      double elapsed = this.ElapsedSeconds();

      bool fired = this.trigger.Fired(
        options.Button, options.Trigger, options.Level, options.Interval);
      bool cleared = this.trigger.Cleared();
      if (cleared) {
        this.state.Reset();
        this.fallbackTile = this.topology.NearestTile(
          new Vector2(0.5f, 0.5f));
      }
      if (fired) {
        this.state.StartCascade(
          this.ChooseOriginTile(), options.TileGrouping,
          options.PropagationRule);
      }

      this.state.AdvanceCascade(elapsed * options.CascadeSpeed);
      this.state.DecayPulses(elapsed);
      this.state.AdvanceFlips(elapsed);
      this.PaintMosaic(options);
    }

    private double ElapsedSeconds() {
      if (!this.frameTimer.IsRunning) {
        this.frameTimer.Restart();
        return 0;
      }
      double elapsed = this.frameTimer.Elapsed.TotalSeconds;
      this.frameTimer.Restart();
      return Math.Min(elapsed, 0.1);
    }

    private int ChooseOriginTile() {
      IReadOnlyDictionary<int, OrientationDevice> devices =
        this.orientationInput.OperatorFrameDevices;
      int spotlight = this.environment.SpotlightDeviceId;
      OrientationDevice selected = null;
      if (spotlight >= 0) {
        devices.TryGetValue(spotlight, out selected);
      } else if (spotlight != -2) {
        foreach (KeyValuePair<int, OrientationDevice> device in
            devices.OrderBy(entry => entry.Key)) {
          if (device.Value.isMoving) {
            selected = device.Value;
            break;
          }
        }
      }

      if (selected != null) {
        Vector3 aim = Vector3.Transform(
          OrientationCenter.Spot,
          Quaternion.Conjugate(selected.currentRotation()));
        Vector2 stripAim = StrutLayoutFactory.ProjectSphereToStrip(
          aim, foldAxisToUpperHemisphere: true);
        return this.topology.NearestTile(new Vector2(
          (stripAim.X + 1) * 0.5f,
          (stripAim.Y + 1) * 0.5f));
      }

      int origin = this.fallbackTile;
      this.fallbackTile = (this.fallbackTile + 31) % this.topology.TileCount;
      return origin;
    }

    private void PaintMosaic(GlassMosaicLayerOptions options) {
      this.ClearBuffer();
      for (int edgeIndex = 0;
          edgeIndex < this.topology.EdgeCount; edgeIndex++) {
        GlassMosaicEdge edge = this.topology.EdgeAt(edgeIndex);
        int[] pixels = this.strutPixels[edge.Strut];
        if (pixels.Length == 0 || edge.Tiles.Length == 0) {
          continue;
        }
        int tileA = edge.Tiles[0];
        int tileB = edge.Tiles.Length > 1 ? edge.Tiles[1] : tileA;
        GlassMosaicTileAppearance appearanceA = TileAppearance(
          this.state.PreviousPhaseAt(tileA), this.state.PhaseAt(tileA),
          this.state.FlipProgressAt(tileA), options.TileTransition);
        GlassMosaicTileAppearance appearanceB = TileAppearance(
          this.state.PreviousPhaseAt(tileB), this.state.PhaseAt(tileB),
          this.state.FlipProgressAt(tileB), options.TileTransition);
        double brightnessA = TileBrightness(
          options.BorderBrightness, this.state.PulseAt(tileA)) *
          appearanceA.Visibility;
        double brightnessB = TileBrightness(
          options.BorderBrightness, this.state.PulseAt(tileB)) *
          appearanceB.Visibility;
        int phaseA = appearanceA.Phase;
        int phaseB = appearanceB.Phase;

        for (int led = 0; led < pixels.Length; led++) {
          double position = (led + 0.5) / pixels.Length;
          int colorA = ScaleColor(this.dome.GetGradientColor(
            phaseA, position, 0, true, options.Palette), brightnessA);
          int colorB = ScaleColor(this.dome.GetGradientColor(
            phaseB, position, 0, true, options.Palette), brightnessB);
          ref LEDDomeOutputPixel pixel = ref this.buffer.pixels[pixels[led]];
          pixel.color = BlendColor(colorA, colorB, position);
          pixel.hue = ((1 - position) * phaseA + position * phaseB) / 8.0;
        }
      }
    }

    private void ClearBuffer() {
      for (int pixel = 0; pixel < this.buffer.pixels.Length; pixel++) {
        this.buffer.pixels[pixel].Clear();
      }
    }

    internal static double TileBrightness(
      double borderBrightness, double pulse
    ) {
      borderBrightness = Math.Clamp(borderBrightness, 0, 1);
      return borderBrightness +
        (1 - borderBrightness) * Math.Clamp(pulse, 0, 1);
    }

    internal static GlassMosaicTileAppearance TileAppearance(
      int previousPhase, int currentPhase,
      double flipProgress, int transition
    ) {
      flipProgress = Math.Clamp(flipProgress, 0, 1);
      currentPhase &= 7;
      if (transition != 1 || flipProgress >= 1) {
        return new GlassMosaicTileAppearance(currentPhase, 1);
      }
      int visiblePhase = flipProgress < 0.5
        ? previousPhase & 7 : currentPhase;
      double visibility = Math.Abs(flipProgress * 2 - 1);
      return new GlassMosaicTileAppearance(visiblePhase, visibility);
    }

    internal static int BlendColor(int a, int b, double weight) {
      weight = Math.Clamp(weight, 0, 1);
      int r = (int)Math.Round(
        ((a >> 16) & 0xFF) * (1 - weight) +
        ((b >> 16) & 0xFF) * weight);
      int g = (int)Math.Round(
        ((a >> 8) & 0xFF) * (1 - weight) +
        ((b >> 8) & 0xFF) * weight);
      int blue = (int)Math.Round(
        (a & 0xFF) * (1 - weight) + (b & 0xFF) * weight);
      return (r << 16) | (g << 8) | blue;
    }

    private static int ScaleColor(int color, double brightness) {
      brightness = Math.Clamp(brightness, 0, 1);
      int r = (int)Math.Round(((color >> 16) & 0xFF) * brightness);
      int g = (int)Math.Round(((color >> 8) & 0xFF) * brightness);
      int b = (int)Math.Round((color & 0xFF) * brightness);
      return (r << 16) | (g << 8) | b;
    }

    private static int[][] BuildStrutPixels(
      DomeTopology frameTopology, int strutCount
    ) {
      var lists = new List<int>[strutCount];
      for (int strut = 0; strut < lists.Length; strut++) {
        lists[strut] = new List<int>();
      }
      for (int pixel = 0; pixel < frameTopology.PixelCount; pixel++) {
        int strut = frameTopology.PixelAt(pixel).StrutIndex;
        if (strut >= 0 && strut < lists.Length) {
          lists[strut].Add(pixel);
        }
      }
      var result = new int[strutCount][];
      for (int strut = 0; strut < result.Length; strut++) {
        result[strut] = lists[strut].ToArray();
      }
      return result;
    }

    private static Vector2[] BuildVertexPositions(int[,] lines) {
      int vertexCount = 0;
      for (int strut = 0; strut < lines.GetLength(0); strut++) {
        vertexCount = Math.Max(vertexCount,
          Math.Max(lines[strut, 0], lines[strut, 1]) + 1);
      }
      var positions = new Vector2[vertexCount];
      var assigned = new bool[vertexCount];
      for (int strut = 0; strut < lines.GetLength(0); strut++) {
        for (int endpoint = 0; endpoint < 2; endpoint++) {
          int vertex = lines[strut, endpoint];
          if (assigned[vertex]) {
            continue;
          }
          Tuple<double, double> point =
            StrutLayoutFactory.GetProjectedPoint(strut, endpoint);
          positions[vertex] = new Vector2(
            (float)point.Item1, (float)point.Item2);
          assigned[vertex] = true;
        }
      }
      return positions;
    }
  }

  internal readonly record struct GlassMosaicTile(
    int A, int B, int C, ImmutableArray<int> Struts, Vector2 Center);

  internal readonly record struct GlassMosaicEdge(
    int A, int B, int Strut, ImmutableArray<int> Tiles);

  internal readonly record struct GlassMosaicTileAppearance(
    int Phase, double Visibility);

  // Discovers triangular faces as three-vertex cliques in the physical graph.
  // On the deployed geodesic dome that produces 120 faces, with adjacency
  // supplied exactly by the strut shared by two faces. This replaces the
  // legacy hand-entered triangle-neighbor table for new topology-native work.
  internal sealed class GlassMosaicTopology {

    private readonly ImmutableArray<GlassMosaicTile> tiles;
    private readonly ImmutableArray<GlassMosaicEdge> edges;
    private readonly ImmutableArray<int>[] neighbors;
    private readonly ImmutableArray<int>[] vertexNeighbors;

    public int TileCount => this.tiles.Length;
    public int EdgeCount => this.edges.Length;

    public GlassMosaicTopology(
      int[,] lines, IReadOnlyList<Vector2> vertexPositions = null
    ) {
      if (lines == null) {
        throw new ArgumentNullException(nameof(lines));
      }
      if (lines.GetLength(1) != 2 || lines.GetLength(0) == 0) {
        throw new ArgumentException(
          "Mosaic graph edges must have two vertices.", nameof(lines));
      }

      int maxVertex = -1;
      var edgeByVertices = new Dictionary<(int A, int B), int>();
      for (int edge = 0; edge < lines.GetLength(0); edge++) {
        int a = lines[edge, 0];
        int b = lines[edge, 1];
        if (a < 0 || b < 0 || a == b) {
          throw new ArgumentException(
            "Mosaic graph contains an invalid edge.", nameof(lines));
        }
        (int A, int B) key = Ordered(a, b);
        if (!edgeByVertices.TryAdd(key, edge)) {
          throw new ArgumentException(
            "Mosaic graph contains a duplicate edge.", nameof(lines));
        }
        maxVertex = Math.Max(maxVertex, Math.Max(a, b));
      }
      if (vertexPositions != null && vertexPositions.Count <= maxVertex) {
        throw new ArgumentException(
          "Mosaic vertex positions do not cover the graph.",
          nameof(vertexPositions));
      }

      var adjacency = new HashSet<int>[maxVertex + 1];
      for (int vertex = 0; vertex < adjacency.Length; vertex++) {
        adjacency[vertex] = new HashSet<int>();
      }
      foreach ((int A, int B) edge in edgeByVertices.Keys) {
        adjacency[edge.A].Add(edge.B);
        adjacency[edge.B].Add(edge.A);
      }

      var tileBuilder = ImmutableArray.CreateBuilder<GlassMosaicTile>();
      var incidentTiles = new List<int>[lines.GetLength(0)];
      for (int edge = 0; edge < incidentTiles.Length; edge++) {
        incidentTiles[edge] = new List<int>(2);
      }
      for (int a = 0; a < adjacency.Length; a++) {
        foreach (int b in adjacency[a].Where(vertex => vertex > a)
            .OrderBy(vertex => vertex)) {
          foreach (int c in adjacency[a].Where(vertex => vertex > b)
              .OrderBy(vertex => vertex)) {
            if (!adjacency[b].Contains(c)) {
              continue;
            }
            int ab = edgeByVertices[Ordered(a, b)];
            int bc = edgeByVertices[Ordered(b, c)];
            int ca = edgeByVertices[Ordered(c, a)];
            int tile = tileBuilder.Count;
            var struts = ImmutableArray.Create(ab, bc, ca);
            tileBuilder.Add(new GlassMosaicTile(
              a, b, c, struts,
              CenterOf(a, b, c, adjacency.Length, vertexPositions)));
            incidentTiles[ab].Add(tile);
            incidentTiles[bc].Add(tile);
            incidentTiles[ca].Add(tile);
          }
        }
      }
      this.tiles = tileBuilder.ToImmutable();
      if (this.tiles.Length == 0) {
        throw new ArgumentException(
          "Mosaic graph contains no triangular faces.", nameof(lines));
      }

      var edgeBuilder = ImmutableArray.CreateBuilder<GlassMosaicEdge>(
        lines.GetLength(0));
      var pendingNeighbors = new HashSet<int>[this.tiles.Length];
      for (int tile = 0; tile < pendingNeighbors.Length; tile++) {
        pendingNeighbors[tile] = new HashSet<int>();
      }
      for (int strut = 0; strut < lines.GetLength(0); strut++) {
        if (incidentTiles[strut].Count > 2) {
          throw new ArgumentException(
            "Mosaic strut belongs to more than two triangular faces.",
            nameof(lines));
        }
        incidentTiles[strut].Sort();
        ImmutableArray<int> edgeTiles =
          incidentTiles[strut].ToImmutableArray();
        edgeBuilder.Add(new GlassMosaicEdge(
          lines[strut, 0], lines[strut, 1], strut, edgeTiles));
        if (edgeTiles.Length == 2) {
          pendingNeighbors[edgeTiles[0]].Add(edgeTiles[1]);
          pendingNeighbors[edgeTiles[1]].Add(edgeTiles[0]);
        }
      }
      this.edges = edgeBuilder.MoveToImmutable();
      this.neighbors = new ImmutableArray<int>[this.tiles.Length];
      for (int tile = 0; tile < this.tiles.Length; tile++) {
        this.neighbors[tile] = pendingNeighbors[tile]
          .OrderBy(neighbor => neighbor).ToImmutableArray();
      }

      // A second, wider neighborhood is useful to cellular simulations. It
      // remains topology-native: two faces are neighbors only when they share
      // a physical dome vertex, even if they do not share a strut.
      var incidentVertexTiles = new List<int>[adjacency.Length];
      for (int vertex = 0; vertex < incidentVertexTiles.Length; vertex++) {
        incidentVertexTiles[vertex] = new List<int>();
      }
      for (int tile = 0; tile < this.tiles.Length; tile++) {
        GlassMosaicTile face = this.tiles[tile];
        incidentVertexTiles[face.A].Add(tile);
        incidentVertexTiles[face.B].Add(tile);
        incidentVertexTiles[face.C].Add(tile);
      }
      this.vertexNeighbors = new ImmutableArray<int>[this.tiles.Length];
      for (int tile = 0; tile < this.tiles.Length; tile++) {
        GlassMosaicTile face = this.tiles[tile];
        var surrounding = new HashSet<int>();
        surrounding.UnionWith(incidentVertexTiles[face.A]);
        surrounding.UnionWith(incidentVertexTiles[face.B]);
        surrounding.UnionWith(incidentVertexTiles[face.C]);
        surrounding.Remove(tile);
        this.vertexNeighbors[tile] = surrounding
          .OrderBy(neighbor => neighbor).ToImmutableArray();
      }
    }

    public GlassMosaicTile TileAt(int tile) {
      this.ValidateTile(tile);
      return this.tiles[tile];
    }

    public GlassMosaicEdge EdgeAt(int edge) {
      if (edge < 0 || edge >= this.EdgeCount) {
        throw new ArgumentOutOfRangeException(nameof(edge));
      }
      return this.edges[edge];
    }

    public ImmutableArray<int> NeighborsAt(int tile) {
      this.ValidateTile(tile);
      return this.neighbors[tile];
    }

    public ImmutableArray<int> VertexNeighborsAt(int tile) {
      this.ValidateTile(tile);
      return this.vertexNeighbors[tile];
    }

    public int NearestTile(Vector2 point) {
      int nearest = 0;
      double bestDistance = double.MaxValue;
      for (int tile = 0; tile < this.tiles.Length; tile++) {
        double distance = Vector2.DistanceSquared(
          point, this.tiles[tile].Center);
        if (distance < bestDistance) {
          bestDistance = distance;
          nearest = tile;
        }
      }
      return nearest;
    }

    private void ValidateTile(int tile) {
      if (tile < 0 || tile >= this.TileCount) {
        throw new ArgumentOutOfRangeException(nameof(tile));
      }
    }

    private static (int A, int B) Ordered(int a, int b) =>
      a < b ? (a, b) : (b, a);

    private static Vector2 CenterOf(
      int a, int b, int c, int vertexCount,
      IReadOnlyList<Vector2> vertexPositions
    ) {
      if (vertexPositions != null) {
        return (vertexPositions[a] + vertexPositions[b] +
          vertexPositions[c]) / 3;
      }
      return (FallbackPosition(a, vertexCount) +
        FallbackPosition(b, vertexCount) +
        FallbackPosition(c, vertexCount)) / 3;
    }

    private static Vector2 FallbackPosition(int vertex, int vertexCount) {
      double angle = 2 * Math.PI * vertex / Math.Max(1, vertexCount);
      return new Vector2(
        (float)(0.5 + 0.45 * Math.Cos(angle)),
        (float)(0.5 + 0.45 * Math.Sin(angle)));
    }
  }

  // Testable persistent tile state. Cascades operate on a deterministic
  // connected partition, so grouping never turns a wave into disconnected
  // teleports. The selected propagation rule only changes the traversal order.
  internal sealed class GlassMosaicState {

    internal const double FlipDurationSeconds = 0.36;

    private sealed class TileGroup {
      public int Id;
      public int[] Tiles;
      public HashSet<int> Neighbors = new HashSet<int>();
      public double Angle;
    }

    private readonly GlassMosaicTopology topology;
    private readonly Random random;
    private readonly int[] phases;
    private readonly int[] previousPhases;
    private readonly double[] pulses;
    private readonly double[] flipProgress;
    private TileGroup[] groups = Array.Empty<TileGroup>();
    private int[] groupOrder = Array.Empty<int>();
    private int nextGroup;
    private double tileBudget;

    public ImmutableArray<int> LastCascadeTileOrder { get; private set; } =
      ImmutableArray<int>.Empty;

    public GlassMosaicState(
      GlassMosaicTopology topology, int randomSeed = 0
    ) {
      this.topology = topology ??
        throw new ArgumentNullException(nameof(topology));
      this.random = new Random(randomSeed);
      this.phases = new int[topology.TileCount];
      this.previousPhases = new int[topology.TileCount];
      this.pulses = new double[topology.TileCount];
      this.flipProgress = new double[topology.TileCount];
      this.Reset();
    }

    public int PhaseAt(int tile) {
      this.ValidateTile(tile);
      return this.phases[tile];
    }

    public double PulseAt(int tile) {
      this.ValidateTile(tile);
      return this.pulses[tile];
    }

    public int PreviousPhaseAt(int tile) {
      this.ValidateTile(tile);
      return this.previousPhases[tile];
    }

    public double FlipProgressAt(int tile) {
      this.ValidateTile(tile);
      return this.flipProgress[tile];
    }

    public void Reset() {
      for (int tile = 0; tile < this.phases.Length; tile++) {
        this.phases[tile] = tile & 7;
        this.previousPhases[tile] = this.phases[tile];
        this.pulses[tile] = 0;
        this.flipProgress[tile] = 1;
      }
      this.groups = Array.Empty<TileGroup>();
      this.groupOrder = Array.Empty<int>();
      this.nextGroup = 0;
      this.tileBudget = 0;
      this.LastCascadeTileOrder = ImmutableArray<int>.Empty;
    }

    public void StartCascade(
      int originTile, int tileGrouping, int propagationRule
    ) {
      this.ValidateTile(originTile);
      this.groups = this.BuildGroups(Math.Clamp(
        tileGrouping, 1, this.topology.TileCount));
      int originGroup = this.groups.First(group =>
        Array.IndexOf(group.Tiles, originTile) >= 0).Id;
      this.groupOrder = this.BuildTraversal(
        originGroup, Math.Clamp(propagationRule, 0, 2));
      var order = ImmutableArray.CreateBuilder<int>(this.topology.TileCount);
      foreach (int group in this.groupOrder) {
        order.AddRange(this.groups[group].Tiles);
      }
      this.LastCascadeTileOrder = order.MoveToImmutable();
      this.nextGroup = 0;
      this.tileBudget = 0;
      if (this.groupOrder.Length > 0) {
        this.ApplyGroup(this.groupOrder[0]);
        this.nextGroup = 1;
      }
    }

    public void AdvanceCascade(double tiles) {
      this.tileBudget += Math.Max(0, tiles);
      while (this.nextGroup < this.groupOrder.Length) {
        int group = this.groupOrder[this.nextGroup];
        int cost = this.groups[group].Tiles.Length;
        if (this.tileBudget + 0.0000001 < cost) {
          break;
        }
        this.tileBudget -= cost;
        this.ApplyGroup(group);
        this.nextGroup++;
      }
    }

    public void DecayPulses(double elapsed) {
      double decay = Math.Max(0, elapsed) / 0.65;
      for (int tile = 0; tile < this.pulses.Length; tile++) {
        this.pulses[tile] = Math.Max(0, this.pulses[tile] - decay);
      }
    }

    public void AdvanceFlips(double elapsed) {
      double advance = Math.Max(0, elapsed) / FlipDurationSeconds;
      for (int tile = 0; tile < this.flipProgress.Length; tile++) {
        this.flipProgress[tile] = Math.Min(
          1, this.flipProgress[tile] + advance);
      }
    }

    private TileGroup[] BuildGroups(int size) {
      var tileToGroup = new int[this.topology.TileCount];
      Array.Fill(tileToGroup, -1);
      var result = new List<TileGroup>();
      for (int seed = 0; seed < this.topology.TileCount; seed++) {
        if (tileToGroup[seed] != -1) {
          continue;
        }
        var members = new List<int>(size);
        var queued = new HashSet<int> { seed };
        var queue = new Queue<int>();
        queue.Enqueue(seed);
        while (queue.Count > 0 && members.Count < size) {
          int tile = queue.Dequeue();
          if (tileToGroup[tile] != -1) {
            continue;
          }
          tileToGroup[tile] = result.Count;
          members.Add(tile);
          foreach (int neighbor in this.topology.NeighborsAt(tile)) {
            if (tileToGroup[neighbor] == -1 && queued.Add(neighbor)) {
              queue.Enqueue(neighbor);
            }
          }
        }
        Vector2 center = Vector2.Zero;
        foreach (int tile in members) {
          center += this.topology.TileAt(tile).Center;
        }
        center /= members.Count;
        result.Add(new TileGroup {
          Id = result.Count,
          Tiles = members.ToArray(),
          Angle = Math.Atan2(center.Y - 0.5, center.X - 0.5),
        });
      }

      for (int tile = 0; tile < this.topology.TileCount; tile++) {
        int group = tileToGroup[tile];
        foreach (int neighbor in this.topology.NeighborsAt(tile)) {
          int neighborGroup = tileToGroup[neighbor];
          if (group != neighborGroup) {
            result[group].Neighbors.Add(neighborGroup);
          }
        }
      }
      return result.ToArray();
    }

    private int[] BuildTraversal(int originGroup, int rule) {
      var order = new List<int>(this.groups.Length);
      var visited = new bool[this.groups.Length];
      this.TraverseComponent(originGroup, rule, visited, order);
      for (int group = 0; group < this.groups.Length; group++) {
        if (!visited[group]) {
          this.TraverseComponent(group, rule, visited, order);
        }
      }
      return order.ToArray();
    }

    private void TraverseComponent(
      int origin, int rule, bool[] visited, List<int> order
    ) {
      var queue = new Queue<int>();
      visited[origin] = true;
      queue.Enqueue(origin);
      while (queue.Count > 0) {
        int group = queue.Dequeue();
        order.Add(group);
        List<int> neighbors = this.groups[group].Neighbors
          .Where(neighbor => !visited[neighbor]).ToList();
        if (rule == 1) {
          neighbors.Sort((a, b) => ClockwiseDelta(
            this.groups[group].Angle, this.groups[a].Angle).CompareTo(
              ClockwiseDelta(
                this.groups[group].Angle, this.groups[b].Angle)));
        } else if (rule == 2) {
          for (int index = neighbors.Count - 1; index > 0; index--) {
            int swap = this.random.Next(index + 1);
            (neighbors[index], neighbors[swap]) =
              (neighbors[swap], neighbors[index]);
          }
        } else {
          neighbors.Sort();
        }
        foreach (int neighbor in neighbors) {
          if (!visited[neighbor]) {
            visited[neighbor] = true;
            queue.Enqueue(neighbor);
          }
        }
      }
    }

    private void ApplyGroup(int group) {
      foreach (int tile in this.groups[group].Tiles) {
        this.previousPhases[tile] = this.phases[tile];
        this.phases[tile] = (this.phases[tile] + 1) & 7;
        this.pulses[tile] = 1;
        this.flipProgress[tile] = 0;
      }
    }

    private void ValidateTile(int tile) {
      if (tile < 0 || tile >= this.topology.TileCount) {
        throw new ArgumentOutOfRangeException(nameof(tile));
      }
    }

    private static double ClockwiseDelta(double from, double to) {
      double delta = from - to;
      while (delta < 0) { delta += 2 * Math.PI; }
      while (delta >= 2 * Math.PI) { delta -= 2 * Math.PI; }
      return delta;
    }
  }
}

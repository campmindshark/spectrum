using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;

namespace Spectrum {

  // A binary cellular automaton whose cells are the dome's physical
  // triangular faces. The installation carries LEDs on face borders, so each
  // strut blends the age-colored state of its one or two incident cells. This
  // makes discrete generations expose the geodesic topology without
  // pretending that the dome is a rectangular texture.
  //
  // Wand buttons are deliberately immediate performance tools: 1 seeds, 2
  // erases, and 3 mutates the aimed colony. Fire seeds a deterministic roaming
  // origin and Clear empties all live state. Timed and beat-driven generation
  // modes share the same persistent automaton owned by this layer instance.
  class LEDDomeCellularDomeVisualizer : DomeLayerVisualizer {

    private const int MaxStepsPerFrame = 8;
    private const int RuleCount = 4;
    private const int SeedButton = 1;
    private const int EraseButton = 2;
    private const int MutateButton = 3;

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private readonly OrientationInput orientationInput;
    private readonly BeatBroadcaster beats;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly LayerTrigger actionTrigger;
    private readonly GlassMosaicTopology topology;
    private readonly CellularDomeState state;
    private readonly int[][] strutPixels;
    private readonly Stopwatch frameTimer = new Stopwatch();
    private readonly Dictionary<int, (int Action, int Tile)> lastWandBrushes =
      new Dictionary<int, (int Action, int Tile)>();
    private double stepAccumulator;
    private double lastBeatProgress = -1;
    private int configuredRule = -1;
    private int configuredTriggerMode = -1;
    private int activeRule;
    private int fallbackTile = 11;

    public LEDDomeCellularDomeVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      OrientationInput orientationInput,
      BeatBroadcaster beats,
      DomeRenderContext dome
    ) {
      this.environment = environment;
      this.runtime = runtime;
      this.orientationInput = orientationInput;
      this.beats = beats;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.actionTrigger = new LayerTrigger(
        environment, null, runtime.InstanceId);
      this.topology = new GlassMosaicTopology(
        StrutLayoutFactory.lines,
        BuildVertexPositions(StrutLayoutFactory.lines));
      this.state = new CellularDomeState(this.topology);
      this.state.Initialize();
      this.strutPixels = BuildStrutPixels(
        this.buffer.Topology, StrutLayoutFactory.lines.GetLength(0));
      this.fallbackTile %= this.topology.TileCount;
    }

    public int Priority => 2;
    public string LayerKey => "cellular-dome";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[]? inputs;
    public Input[] GetInputs() =>
      this.inputs ?? (this.inputs = new Input[] { this.orientationInput });

    public void Visualize() {
      CellularDomeLayerOptions options =
        this.runtime.GetOptions<CellularDomeLayerOptions>();
      double elapsed = this.ElapsedSeconds();

      if (this.configuredRule != options.Rule ||
          this.configuredTriggerMode != options.TriggerMode) {
        this.configuredRule = options.Rule;
        if (this.configuredTriggerMode != options.TriggerMode) {
          this.stepAccumulator = 0;
        }
        this.configuredTriggerMode = options.TriggerMode;
        this.activeRule = options.Rule;
      }

      bool fired = this.actionTrigger.Fired(0);
      bool cleared = this.actionTrigger.Cleared();
      if (cleared) {
        this.state.Clear();
        this.stepAccumulator = 0;
      }
      if (fired) {
        this.state.SeedAt(
          this.NextFallbackTile(), 1, options.Neighborhood);
      }

      bool beatWrapped = this.BeatWrapped();
      if (options.TriggerMode == 0) {
        this.AdvanceTimed(
          elapsed, options.GenerationRate, options.Neighborhood);
      } else if (beatWrapped) {
        if (options.TriggerMode == 2) {
          this.activeRule = (this.activeRule + 1) % RuleCount;
        }
        this.state.Step(this.activeRule, options.Neighborhood);
      }

      this.ApplyWandBrushes(options.Neighborhood);
      this.state.AdvanceAppearance(elapsed, options.AgeDecay);
      this.Paint(options);
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

    private bool BeatWrapped() {
      double progress = this.beats.ProgressThroughMeasure;
      bool wrapped = this.lastBeatProgress >= 0 &&
        progress < this.lastBeatProgress;
      this.lastBeatProgress = progress;
      return wrapped;
    }

    private void AdvanceTimed(
      double elapsed, double generationRate, int neighborhood
    ) {
      this.stepAccumulator += elapsed * Math.Max(0, generationRate);
      int steps = Math.Min(MaxStepsPerFrame, (int)this.stepAccumulator);
      if (steps == MaxStepsPerFrame && this.stepAccumulator > steps) {
        // Do not spend a long frame replaying generations the audience never
        // saw. Keep only the fractional cadence for the next frame.
        this.stepAccumulator -= Math.Floor(this.stepAccumulator);
      } else {
        this.stepAccumulator -= steps;
      }
      for (int step = 0; step < steps; step++) {
        this.state.Step(this.activeRule, neighborhood);
      }
    }

    private void ApplyWandBrushes(int neighborhood) {
      IReadOnlyDictionary<int, OrientationDevice> devices =
        this.orientationInput.OperatorFrameDevices;
      int spotlight = this.environment.SpotlightDeviceId;
      if (spotlight == -2) {
        this.lastWandBrushes.Clear();
        return;
      }
      bool spotlightMoving = spotlight >= 0 &&
        devices.TryGetValue(
          spotlight, out OrientationDevice? spotlightDevice) &&
        spotlightDevice != null &&
        spotlightDevice.isMoving;

      foreach (KeyValuePair<int, OrientationDevice> entry in devices) {
        OrientationDevice device = entry.Value;
        if (!device.isMoving ||
            (spotlightMoving && entry.Key != spotlight)) {
          if (device.actionFlag == 0) {
            this.lastWandBrushes[entry.Key] = (0, -1);
          }
          continue;
        }

        int action = device.actionFlag;
        if (action != SeedButton && action != EraseButton &&
            action != MutateButton) {
          this.lastWandBrushes[entry.Key] = (0, -1);
          continue;
        }
        int tile = this.AimedTile(device);
        this.lastWandBrushes.TryGetValue(
          entry.Key, out (int Action, int Tile) previous);

        if (action == SeedButton) {
          this.state.SeedAt(tile, 1, neighborhood);
        } else if (action == EraseButton) {
          this.state.EraseAt(tile, 1, neighborhood);
        } else if (previous.Action != MutateButton ||
            previous.Tile != tile) {
          // Mutate is edge-like per aimed cell. Toggling every render frame
          // while a stationary button is held would only produce flicker.
          this.state.MutateAt(tile, 1, neighborhood);
        }
        this.lastWandBrushes[entry.Key] = (action, tile);
      }
    }

    private int AimedTile(OrientationDevice device) {
      Vector3 aim = Vector3.Transform(
        OrientationCenter.Spot,
        Quaternion.Conjugate(device.currentRotation()));
      Vector2 stripAim = StrutLayoutFactory.ProjectSphereToStrip(
        aim, foldAxisToUpperHemisphere: true);
      return this.topology.NearestTile(new Vector2(
        (stripAim.X + 1) * 0.5f,
        (stripAim.Y + 1) * 0.5f));
    }

    private int NextFallbackTile() {
      int tile = this.fallbackTile;
      this.fallbackTile = (this.fallbackTile + 37) % this.topology.TileCount;
      return tile;
    }

    private void Paint(CellularDomeLayerOptions options) {
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
        double brightnessA = this.state.BrightnessAt(tileA);
        double brightnessB = this.state.BrightnessAt(tileB);
        double alpha = Math.Max(brightnessA, brightnessB);
        if (alpha < 0.0001) {
          continue;
        }
        int phaseA = this.state.ColorPhaseAt(
          tileA, options.BirthColor, options.AgeDecay);
        int phaseB = this.state.ColorPhaseAt(
          tileB, options.BirthColor, options.AgeDecay);

        for (int led = 0; led < pixels.Length; led++) {
          double position = (led + 0.5) / pixels.Length;
          int colorA = ScaleColor(this.dome.GetGradientColor(
            phaseA, position, 0, true, options.Palette), brightnessA);
          int colorB = ScaleColor(this.dome.GetGradientColor(
            phaseB, position, 0, true, options.Palette), brightnessB);
          ref LEDDomeOutputPixel pixel = ref this.buffer.pixels[pixels[led]];
          pixel.color = BlendColor(colorA, colorB, position);
          pixel.SetAlpha(alpha);
          pixel.hue = ((1 - position) * phaseA + position * phaseB) / 8.0;
        }
      }
    }

    private void ClearBuffer() {
      for (int pixel = 0; pixel < this.buffer.pixels.Length; pixel++) {
        this.buffer.pixels[pixel].Clear();
        this.buffer.pixels[pixel].hue = 0;
      }
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

  internal enum CellularDomeMutation {
    Seed,
    Erase,
    Toggle,
  }

  // Persistent, clock-independent automaton state. Keeping the binary update
  // and appearance decay separate makes beat stepping deterministic while
  // still allowing old cells and recently dead fronts to visibly recede in
  // wall-clock time.
  internal sealed class CellularDomeState {

    private const double OldCellBrightness = 0.18;

    private readonly GlassMosaicTopology topology;
    private bool[] alive;
    private bool[] next;
    private readonly double[] ages;
    private readonly double[] brightness;

    public int AliveCount { get; private set; }

    public CellularDomeState(GlassMosaicTopology topology) {
      this.topology = topology ??
        throw new ArgumentNullException(nameof(topology));
      this.alive = new bool[topology.TileCount];
      this.next = new bool[topology.TileCount];
      this.ages = new double[topology.TileCount];
      this.brightness = new double[topology.TileCount];
    }

    public void Initialize() {
      this.Clear();
      int colonyCount = Math.Min(7, Math.Max(1, this.topology.TileCount / 12));
      for (int colony = 0; colony < colonyCount; colony++) {
        int origin = (17 + colony * 37) % this.topology.TileCount;
        this.SeedAt(origin, 1, 0);
      }
    }

    public void Clear() {
      Array.Clear(this.alive, 0, this.alive.Length);
      Array.Clear(this.next, 0, this.next.Length);
      Array.Clear(this.ages, 0, this.ages.Length);
      Array.Clear(this.brightness, 0, this.brightness.Length);
      this.AliveCount = 0;
    }

    public bool AliveAt(int tile) {
      this.ValidateTile(tile);
      return this.alive[tile];
    }

    public double AgeAt(int tile) {
      this.ValidateTile(tile);
      return this.ages[tile];
    }

    public double BrightnessAt(int tile) {
      this.ValidateTile(tile);
      return this.brightness[tile];
    }

    public int ColorPhaseAt(int tile, int birthColor, double ageDecay) {
      this.ValidateTile(tile);
      ageDecay = Math.Max(0.0001, ageDecay);
      int agePhase = Math.Min(7,
        (int)Math.Floor(this.ages[tile] / ageDecay));
      return (Math.Clamp(birthColor, 0, 7) + agePhase) & 7;
    }

    public void SeedAt(int center, int radius, int neighborhood) =>
      this.ApplyAt(
        center, radius, neighborhood, CellularDomeMutation.Seed);

    public void EraseAt(int center, int radius, int neighborhood) =>
      this.ApplyAt(
        center, radius, neighborhood, CellularDomeMutation.Erase);

    public void MutateAt(int center, int radius, int neighborhood) =>
      this.ApplyAt(
        center, radius, neighborhood, CellularDomeMutation.Toggle);

    public void Step(int rule, int neighborhood) {
      rule = Math.Clamp(rule, 0, 3);
      this.ValidateNeighborhood(neighborhood);
      int liveTotal = 0;
      for (int tile = 0; tile < this.alive.Length; tile++) {
        ImmutableArray<int> neighbors =
          this.NeighborsAt(tile, neighborhood);
        int liveNeighbors = 0;
        foreach (int neighbor in neighbors) {
          if (this.alive[neighbor]) {
            liveNeighbors++;
          }
        }
        this.next[tile] = NextAlive(
          rule, this.alive[tile], liveNeighbors);
        if (this.next[tile]) {
          liveTotal++;
          if (!this.alive[tile]) {
            this.ages[tile] = 0;
            this.brightness[tile] = 1;
          }
        }
      }
      (this.alive, this.next) = (this.next, this.alive);
      this.AliveCount = liveTotal;
    }

    internal static bool NextAlive(
      int rule, bool currentlyAlive, int liveNeighbors
    ) {
      liveNeighbors = Math.Max(0, liveNeighbors);
      return rule switch {
        // Triangular-life colony rule: a two-neighbor birth with broad enough
        // survival to leave stable islands on three-edge neighborhoods.
        0 => currentlyAlive
          ? liveNeighbors == 1 || liveNeighbors == 2
          : liveNeighbors == 2,
        // Exact-one births with no survival produce clean alternating shells.
        1 => !currentlyAlive && liveNeighbors == 1,
        // Existing cells persist and every neighboring dead cell joins them,
        // creating an unmistakable traveling front across the face graph.
        2 => currentlyAlive || liveNeighbors > 0,
        // Extra one- and three-neighbor births destabilize colony boundaries.
        3 => currentlyAlive
          ? liveNeighbors == 1 || liveNeighbors == 2
          : liveNeighbors == 1 || liveNeighbors == 3,
        _ => false,
      };
    }

    public void AdvanceAppearance(double elapsed, double ageDecay) {
      elapsed = Math.Max(0, elapsed);
      ageDecay = Math.Max(0.0001, ageDecay);
      double decay = Math.Pow(0.5, elapsed / ageDecay);
      for (int tile = 0; tile < this.alive.Length; tile++) {
        if (this.alive[tile]) {
          this.ages[tile] += elapsed;
          this.brightness[tile] = OldCellBrightness +
            (1 - OldCellBrightness) *
            Math.Pow(0.5, this.ages[tile] / ageDecay);
        } else {
          this.brightness[tile] *= decay;
          if (this.brightness[tile] < 0.000001) {
            this.brightness[tile] = 0;
          }
        }
      }
    }

    private void ApplyAt(
      int center, int radius, int neighborhood, CellularDomeMutation mutation
    ) {
      this.ValidateTile(center);
      this.ValidateNeighborhood(neighborhood);
      radius = Math.Clamp(radius, 0, this.topology.TileCount);
      var visited = new HashSet<int> { center };
      var queue = new Queue<(int Tile, int Distance)>();
      queue.Enqueue((center, 0));
      while (queue.Count > 0) {
        (int tile, int distance) = queue.Dequeue();
        this.ApplyMutation(tile, mutation);
        if (distance >= radius) {
          continue;
        }
        foreach (int neighbor in this.NeighborsAt(tile, neighborhood)) {
          if (visited.Add(neighbor)) {
            queue.Enqueue((neighbor, distance + 1));
          }
        }
      }
      this.AliveCount = 0;
      for (int tile = 0; tile < this.alive.Length; tile++) {
        if (this.alive[tile]) {
          this.AliveCount++;
        }
      }
    }

    private void ApplyMutation(int tile, CellularDomeMutation mutation) {
      if (mutation == CellularDomeMutation.Erase) {
        this.alive[tile] = false;
        this.ages[tile] = 0;
        this.brightness[tile] = 0;
        return;
      }
      bool makeAlive = mutation == CellularDomeMutation.Seed ||
        !this.alive[tile];
      if (makeAlive) {
        this.alive[tile] = true;
        this.ages[tile] = 0;
        this.brightness[tile] = 1;
      } else {
        // A toggled-off cell keeps its current light as an afterimage; Erase
        // remains the explicitly hard, transparent removal tool.
        this.alive[tile] = false;
      }
    }

    private ImmutableArray<int> NeighborsAt(int tile, int neighborhood) =>
      neighborhood == 0
        ? this.topology.NeighborsAt(tile)
        : this.topology.VertexNeighborsAt(tile);

    private void ValidateTile(int tile) {
      if (tile < 0 || tile >= this.topology.TileCount) {
        throw new ArgumentOutOfRangeException(nameof(tile));
      }
    }

    private void ValidateNeighborhood(int neighborhood) {
      if (neighborhood < 0 || neighborhood > 1) {
        throw new ArgumentOutOfRangeException(nameof(neighborhood));
      }
    }
  }
}

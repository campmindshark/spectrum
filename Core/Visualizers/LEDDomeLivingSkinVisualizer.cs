using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;

namespace Spectrum.Visualizers {

  // A persistent Gray-Scott reaction-diffusion field evaluated directly on
  // the physical dome pixels. The simulation uses DomeTopology's shared
  // spatial-neighbor table, so chemicals cross nearby struts without a second
  // raster grid or a projection/sampling seam. Each configured layer owns its
  // own chemical arrays and therefore keeps evolving across scene recalls.
  //
  // Deterministic seeds arrive at construction, on beats, and through the
  // generic Fire action. Clear removes chemical B, leaving the uniform A field
  // dormant until another seed arrives. Held wand buttons apply graph-local
  // feed, poison, and erase brushes over the shared baked-neighbor rings.
  class LEDDomeLivingSkinVisualizer : DomeLayerVisualizer {

    private const double SimulationStepsPerSecond = 30;
    private const int MaxStepsPerFrame = 8;
    private const int InitialSeedCount = 13;

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private readonly OrientationInput orientationInput;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly ImmutableArray<Vector3> pixelPositions;
    private readonly LivingSkinSimulation simulation;
    private readonly LayerTrigger seedTrigger;
    private readonly System.Diagnostics.Stopwatch frameTimer =
      new System.Diagnostics.Stopwatch();
    private double stepAccumulator;

    public LEDDomeLivingSkinVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      OrientationInput orientationInput,
      BeatBroadcaster beats,
      DomeRenderContext dome
    ) {
      this.environment = environment;
      this.runtime = runtime;
      this.orientationInput = orientationInput;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.pixelPositions = this.buffer.BakePixelPositions();
      this.simulation = new LivingSkinSimulation(this.buffer);
      this.simulation.Initialize(InitialSeedCount);
      this.seedTrigger = new LayerTrigger(
        environment, null, runtime.InstanceId, beats);
    }

    public int Priority => 2;
    public string LayerKey => "living-skin";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() =>
      this.inputs ?? (this.inputs = new Input[] { this.orientationInput });

    public void Visualize() {
      LivingSkinLayerOptions options =
        this.runtime.GetOptions<LivingSkinLayerOptions>();

      // seedSource uses the shared trigger-source numbering for the two modes
      // it exposes: 0 keeps only initial/manual seeds; 1 additionally watches
      // BeatBroadcaster. Manual Fire remains active in both modes.
      if (this.seedTrigger.Fired(
          0, options.SeedSource, 0, 0)) {
        this.simulation.SeedNext();
      }
      if (this.seedTrigger.Cleared()) {
        this.simulation.Clear();
        this.stepAccumulator = 0;
      }

      double elapsed = 0;
      if (!this.frameTimer.IsRunning) {
        this.frameTimer.Restart();
      } else {
        elapsed = this.frameTimer.Elapsed.TotalSeconds;
        this.frameTimer.Restart();
      }
      this.Advance(options, elapsed);
      this.ApplyWandBrushes(options);
      this.Paint(options);
    }

    private void ApplyWandBrushes(LivingSkinLayerOptions options) {
      IReadOnlyDictionary<int, OrientationDevice> devices =
        this.orientationInput.OperatorFrameDevices;
      int spotlight = this.environment.SpotlightDeviceId;
      if (spotlight == -2) {
        return;
      }
      bool spotlightMoving = spotlight >= 0
        && devices.TryGetValue(spotlight, out OrientationDevice spotlightDevice)
        && spotlightDevice.isMoving;

      foreach (KeyValuePair<int, OrientationDevice> entry in devices) {
        OrientationDevice device = entry.Value;
        if (!device.isMoving ||
            (spotlightMoving && entry.Key != spotlight)) {
          continue;
        }
        LivingSkinBrushMode? mode = BrushModeForButton(
          device.actionFlag, options);
        if (!mode.HasValue) {
          continue;
        }

        Vector3 aim = Vector3.Transform(
          OrientationCenter.Spot,
          Quaternion.Conjugate(device.currentRotation()));
        this.simulation.BrushAt(
          this.NearestPixel(FoldToUpperHemisphere(aim)),
          options.BrushRadius, options.BrushStrength, mode.Value);
      }
    }

    internal static LivingSkinBrushMode? BrushModeForButton(
      int actionFlag, LivingSkinLayerOptions options
    ) {
      if (actionFlag <= 0) {
        return null;
      }
      // Destructive operations win if bindings overlap, making an emergency
      // erase deterministic even after a preset maps two tools to one button.
      if (actionFlag == options.EraseButton) {
        return LivingSkinBrushMode.Erase;
      }
      if (actionFlag == options.PoisonButton) {
        return LivingSkinBrushMode.Poison;
      }
      return actionFlag == options.FeedButton
        ? LivingSkinBrushMode.Feed
        : null;
    }

    private int NearestPixel(Vector3 aim) {
      int nearest = 0;
      float bestAlignment = float.NegativeInfinity;
      for (int pixel = 0; pixel < this.pixelPositions.Length; pixel++) {
        float alignment = Vector3.Dot(this.pixelPositions[pixel], aim);
        if (alignment > bestAlignment) {
          bestAlignment = alignment;
          nearest = pixel;
        }
      }
      return nearest;
    }

    internal static Vector3 FoldToUpperHemisphere(Vector3 direction) {
      if (direction.LengthSquared() < 1e-8) {
        return Vector3.UnitZ;
      }
      Vector3 normalized = Vector3.Normalize(direction);
      return normalized.Z < 0
        ? new Vector3(normalized.X, normalized.Y, -normalized.Z)
        : normalized;
    }

    private void Advance(LivingSkinLayerOptions options, double elapsed) {
      this.stepAccumulator += elapsed * SimulationStepsPerSecond
        * options.SimulationSpeed;
      int steps = (int)this.stepAccumulator;
      if (steps > MaxStepsPerFrame) {
        // A paused or obstructed render loop must not return by spending a long
        // burst catching up simulation history the audience never observed.
        steps = MaxStepsPerFrame;
        this.stepAccumulator = 0;
      } else {
        this.stepAccumulator -= steps;
      }
      for (int i = 0; i < steps; i++) {
        this.simulation.Step(
          options.FeedRate, options.KillRate, options.DiffusionScale);
      }
    }

    private void Paint(LivingSkinLayerOptions options) {
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        double chemical = this.simulation.ChemicalBAt(i);
        double neighbor = this.simulation.NeighborAverageBAt(
          i, options.DiffusionScale);
        double pigment = SmoothStep(0.04, 0.52, chemical);
        double edge = Math.Clamp(
          Math.Abs(chemical - neighbor) * options.EdgeContrast * 4,
          0, 1);

        // The low palette-tinted baseline keeps this useful as a foundation;
        // chemical bodies add mass and their boundary gets the sharp highlight.
        double luminance = Math.Clamp(
          0.08 + 0.62 * pigment + 0.45 * edge, 0, 1);
        double palettePosition = Math.Clamp(
          0.05 + 0.85 * (0.72 * pigment + 0.28 * edge), 0, 1);
        int tint = this.dome.GetGradientBetweenColors(
          0, 7, palettePosition, 0, true, options.Palette);
        int r = (int)(((tint >> 16) & 0xFF) * luminance);
        int g = (int)(((tint >> 8) & 0xFF) * luminance);
        int b = (int)((tint & 0xFF) * luminance);

        ref LEDDomeOutputPixel pixel = ref this.buffer.pixels[i];
        pixel.color = (r << 16) | (g << 8) | b;
        pixel.SetAlpha(1);
      }
    }

    private static double SmoothStep(double edge0, double edge1, double x) {
      double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
      return t * t * (3 - 2 * t);
    }
  }

  internal enum LivingSkinBrushMode {
    Feed,
    Poison,
    Erase,
  }

  // The reusable stateful core is kept separate from rendering and live-input
  // concerns. Its public-to-assembly sampling methods make the numerical
  // invariants testable without constructing an Operator or waiting on a clock.
  internal sealed class LivingSkinSimulation {

    private readonly DomeFrame surface;
    private double[] chemicalA;
    private double[] chemicalB;
    private double[] nextA;
    private double[] nextB;
    private uint seedState = 0x9E3779B9u;

    public LivingSkinSimulation(DomeFrame surface) {
      this.surface = surface ??
        throw new ArgumentNullException(nameof(surface));
      int count = surface.pixels.Length;
      if (count == 0) {
        throw new ArgumentException(
          "Living Skin needs at least one surface pixel.", nameof(surface));
      }
      this.chemicalA = new double[count];
      this.chemicalB = new double[count];
      this.nextA = new double[count];
      this.nextB = new double[count];
      this.Clear();
    }

    public void Initialize(int seedCount) {
      this.Clear();
      for (int i = 0; i < Math.Max(0, seedCount); i++) {
        this.SeedNext();
      }
    }

    public void Clear() {
      Array.Fill(this.chemicalA, 1);
      Array.Clear(this.chemicalB, 0, this.chemicalB.Length);
      Array.Clear(this.nextA, 0, this.nextA.Length);
      Array.Clear(this.nextB, 0, this.nextB.Length);
    }

    public void SeedNext() {
      // Numerical Recipes' LCG is sufficient here: it is deterministic, cheap,
      // and only selects spatial injection sites (never security-sensitive).
      this.seedState = unchecked(
        this.seedState * 1664525u + 1013904223u);
      this.SeedAt((int)(this.seedState % (uint)this.chemicalA.Length));
    }

    internal void SeedAt(int center) {
      if (center < 0 || center >= this.chemicalA.Length) {
        throw new ArgumentOutOfRangeException(nameof(center));
      }
      this.ApplySeed(center, 1);
      for (int radius = 0; radius < 2; radius++) {
        double strength = radius == 0 ? 0.78 : 0.48;
        for (int direction = 0;
             direction < DomeFrame.NeighborDirections;
             direction++) {
          this.ApplySeed(
            this.surface.NeighborAt(center, direction, radius), strength);
        }
      }
    }

    internal void BrushAt(
      int center, int radius, double strength, LivingSkinBrushMode mode
    ) {
      if (center < 0 || center >= this.chemicalA.Length) {
        throw new ArgumentOutOfRangeException(nameof(center));
      }
      radius = Math.Clamp(radius, 1, DomeFrame.NeighborRadii);
      strength = Math.Clamp(strength, 0, 1);
      this.ApplyBrush(center, strength, mode);
      for (int ring = 0; ring < radius; ring++) {
        double falloff = 1 - 0.65 * (ring + 1) / radius;
        for (int direction = 0;
             direction < DomeFrame.NeighborDirections;
             direction++) {
          this.ApplyBrush(
            this.surface.NeighborAt(center, direction, ring),
            strength * falloff, mode);
        }
      }
    }

    private void ApplyBrush(
      int pixel, double strength, LivingSkinBrushMode mode
    ) {
      switch (mode) {
        case LivingSkinBrushMode.Feed:
          // Restore substrate and leave a small activator trace, so a feed
          // stroke can both nurture an existing colony and paint into dormancy.
          this.chemicalA[pixel] +=
            (1 - this.chemicalA[pixel]) * strength;
          this.chemicalB[pixel] +=
            (1 - this.chemicalB[pixel]) * (0.18 * strength);
          break;
        case LivingSkinBrushMode.Poison:
          // Starve both chemicals without resetting the site; the normal feed
          // term recovers A later, leaving a temporary dark wound.
          this.chemicalA[pixel] *= 1 - strength;
          this.chemicalB[pixel] *= 1 - 0.8 * strength;
          break;
        case LivingSkinBrushMode.Erase:
          this.chemicalA[pixel] +=
            (1 - this.chemicalA[pixel]) * strength;
          this.chemicalB[pixel] *= 1 - strength;
          break;
      }
      this.chemicalA[pixel] = ClampUnit(this.chemicalA[pixel]);
      this.chemicalB[pixel] = ClampUnit(this.chemicalB[pixel]);
    }

    private void ApplySeed(int pixel, double strength) {
      this.chemicalA[pixel] = Math.Min(
        this.chemicalA[pixel], 1 - 0.72 * strength);
      this.chemicalB[pixel] = Math.Max(
        this.chemicalB[pixel], 0.92 * strength);
    }

    public void Step(
      double feedRate, double killRate, double diffusionScale
    ) {
      feedRate = Math.Clamp(feedRate, 0, 1);
      killRate = Math.Clamp(killRate, 0, 1);
      int radius = RadiusBin(diffusionScale);

      for (int i = 0; i < this.chemicalA.Length; i++) {
        double a = this.chemicalA[i];
        double b = this.chemicalB[i];
        double lapA = this.LaplacianAt(this.chemicalA, i, radius);
        double lapB = this.LaplacianAt(this.chemicalB, i, radius);
        double reaction = a * b * b;

        this.nextA[i] = ClampUnit(
          a + lapA - reaction + feedRate * (1 - a));
        this.nextB[i] = ClampUnit(
          b + 0.5 * lapB + reaction - (feedRate + killRate) * b);
      }

      (this.chemicalA, this.nextA) = (this.nextA, this.chemicalA);
      (this.chemicalB, this.nextB) = (this.nextB, this.chemicalB);
    }

    public double ChemicalAAt(int pixel) => this.chemicalA[pixel];
    public double ChemicalBAt(int pixel) => this.chemicalB[pixel];

    public double NeighborAverageBAt(int pixel, double diffusionScale) =>
      this.NeighborAverageAt(
        this.chemicalB, pixel, RadiusBin(diffusionScale));

    private double LaplacianAt(double[] field, int pixel, int radius) =>
      this.NeighborAverageAt(field, pixel, radius) - field[pixel];

    private double NeighborAverageAt(
      double[] field, int pixel, int radius
    ) {
      // DomeTopology has 16 angular bins. Even bins give an eight-neighbor
      // stencil: cardinal directions carry 0.2 each and diagonals 0.05 each,
      // the standard isotropic Gray-Scott weights summing to one.
      double sum = 0;
      for (int direction = 0; direction < 16; direction += 2) {
        double weight = direction % 4 == 0 ? 0.2 : 0.05;
        int neighbor = this.surface.NeighborAt(pixel, direction, radius);
        sum += weight * field[neighbor];
      }
      return sum;
    }

    private static int RadiusBin(double diffusionScale) =>
      Math.Clamp(
        (int)Math.Round(diffusionScale) - 1,
        0, DomeFrame.NeighborRadii - 1);

    private static double ClampUnit(double value) {
      if (double.IsNaN(value)) {
        return 0;
      }
      return Math.Clamp(value, 0, 1);
    }
  }
}

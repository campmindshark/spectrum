using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Immutable;
using System.Numerics;

namespace Spectrum.Visualizers {

  // Concentric rings fly from the dome's crown toward its rim, creating the
  // expanding-circle optic flow of driving through a tunnel. Every ring has a
  // deterministic speed, width, and brightness so the motion stays varied
  // without flickering or allocating particle state. The normalized angular
  // radius is eased quadratically: distant rings emerge slowly at the crown
  // and rush outward as they approach the viewer.
  class LEDDomeTunnelVisualizer : DomeLayerVisualizer {

    private const int MaxRingCount = 24;
    private const double GoldenRatioConjugate = 0.6180339887498949;
    private const double IdleLevel = 0.4;

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private readonly OrientationInput orientationInput;
    private readonly OrientationCenter center;
    private readonly LEDDomeOutput dome;
    private readonly DomeFrame buffer;
    private readonly ImmutableArray<Vector3> pixelPositions;
    private readonly double[] fixedPixelRadii;
    private readonly double[] orientedPixelRadii;
    private readonly RingTrait[] traits = new RingTrait[MaxRingCount];
    private readonly double[] ringRadii = new double[MaxRingCount];
    private readonly double[] ringHalfWidths = new double[MaxRingCount];
    private readonly double[] ringBrightnesses = new double[MaxRingCount];
    private readonly FrameClock frameClock = new FrameClock();

    private double time;

    private readonly struct RingTrait {
      public readonly double Offset;
      public readonly double SpeedFactor;
      public readonly double WidthFactor;
      public readonly double BrightnessFactor;

      public RingTrait(
        double offset, double speedFactor, double widthFactor,
        double brightnessFactor
      ) {
        this.Offset = offset;
        this.SpeedFactor = speedFactor;
        this.WidthFactor = widthFactor;
        this.BrightnessFactor = brightnessFactor;
      }
    }

    public LEDDomeTunnelVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      OrientationInput orientationInput,
      OrientationCenter center,
      LEDDomeOutput dome
    ) {
      this.environment = environment;
      this.runtime = runtime;
      this.orientationInput = orientationInput;
      this.center = center;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeFrame();
      this.pixelPositions = this.buffer.BakePixelPositions();

      // Fixed mode is the same sphere-native angular field as oriented mode,
      // with the crown selected as its axis. Keeping one construction path
      // prevents a mode toggle from shifting every ring radially.
      this.fixedPixelRadii = BuildFixedRadii(this.pixelPositions);
      this.orientedPixelRadii = new double[this.buffer.pixels.Length];

      uint seed = InstanceSeed(runtime.InstanceId.Value);
      for (int i = 0; i < this.traits.Length; i++) {
        double offset = Fraction(
          i * GoldenRatioConjugate + (Hash01(seed, i, 0) - 0.5) * 0.08
        );
        this.traits[i] = new RingTrait(
          offset,
          0.55 + 0.90 * Hash01(seed, i, 1),
          0.45 + 1.20 * Hash01(seed, i, 2),
          0.25 + 0.75 * Hash01(seed, i, 3)
        );
      }
    }

    public int Priority => 2;
    public string LayerKey => "tunnel";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      // Keep the optional dependency declared even while binding is off. Layer
      // parameters update the retained renderer in place, so this lets the
      // checkbox become live immediately without recreating or rescheduling it.
      return this.inputs ??
        (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      TunnelLayerOptions options =
        this.runtime.GetOptions<TunnelLayerOptions>();
      double frameScale = this.frameClock.Tick();
      this.time += frameScale / FrameClock.NominalFps;

      // Match Ripple and Stamp's global trail behavior. At Fade Speed zero the
      // retention is zero, yielding crisp independent frames; raising it turns
      // the outward motion into luminous tunnel streaks.
      double frameRetention =
        1 - Math.Pow(5, -this.environment.GlobalFadeSpeed);
      this.buffer.Fade(Math.Pow(frameRetention, frameScale), 0);

      int ringCount = Math.Clamp(options.RingCount, 1, MaxRingCount);
      BuildRingState(
        ringCount, options.Speed, options.Thickness, options.Brightness,
        options.Variation
      );
      double[] radii = this.fixedPixelRadii;
      if (options.BindToOrientation) {
        // OrientationCenter resolves the configured moving spotlight, else the
        // first moving device (the lead), else the shared idle wanderer.
        this.center.Update(IdleLevel);
        UpdateOrientedRadii();
        radii = this.orientedPixelRadii;
      }
      RenderPixels(ringCount, options.Color, radii);
    }

    private void BuildRingState(
      int ringCount, double speed, double thickness, double brightness,
      double variation
    ) {
      for (int i = 0; i < ringCount; i++) {
        RingTrait trait = this.traits[i];
        double speedFactor = Lerp(1, trait.SpeedFactor, variation);
        double phase = Fraction(trait.Offset + this.time * speed * speedFactor);

        // Quadratic radial growth supplies the apparent acceleration of tunnel
        // markers nearing the viewer. Their width and intensity grow too,
        // reinforcing the same depth cue while retaining per-ring variation.
        this.ringRadii[i] = phase * phase;
        this.ringHalfWidths[i] = thickness
          * Lerp(1, trait.WidthFactor, variation)
          * (0.55 + 0.85 * phase);
        this.ringBrightnesses[i] = brightness
          * Lerp(1, trait.BrightnessFactor, variation)
          * (0.45 + 0.55 * phase);
      }
    }

    private void UpdateOrientedRadii() {
      // CurrentCenter maps a dome point into orientation-local space where
      // Spot is the forward pole. Rotate Spot back into dome space to get the
      // tunnel axis. Orientation is an axis rather than a directed ray, so use
      // whichever antipodal endpoint lies on the dome's positive hemisphere.
      Vector3 axis = Vector3.Transform(
        OrientationCenter.Spot,
        Quaternion.Conjugate(this.center.CurrentCenter)
      );
      if (axis.Z < 0) {
        axis = -axis;
      }
      axis = Vector3.Normalize(axis);
      BuildNormalizedAngularRadii(
        this.pixelPositions, axis, this.orientedPixelRadii);
    }

    internal static double[] BuildFixedRadii(
      ImmutableArray<Vector3> pixelPositions
    ) {
      var radii = new double[pixelPositions.Length];
      BuildNormalizedAngularRadii(pixelPositions, Vector3.UnitZ, radii);
      return radii;
    }

    internal static void BuildNormalizedAngularRadii(
      ImmutableArray<Vector3> pixelPositions,
      Vector3 axis,
      double[] destination
    ) {
      if (pixelPositions.IsDefault) {
        throw new ArgumentException(
          "Tunnel pixel positions must be initialized.",
          nameof(pixelPositions));
      }
      if (destination == null || destination.Length != pixelPositions.Length) {
        throw new ArgumentException(
          "Tunnel radius output must match the pixel count.",
          nameof(destination));
      }
      float axisLengthSquared = axis.LengthSquared();
      if (!float.IsFinite(axisLengthSquared) || axisLengthSquared <= 0) {
        throw new ArgumentException(
          "Tunnel axis must be finite and non-zero.", nameof(axis));
      }
      axis = Vector3.Normalize(axis);

      double maxAngle = 0;
      for (int i = 0; i < pixelPositions.Length; i++) {
        double angle = AngularDistance(pixelPositions[i], axis);
        destination[i] = angle;
        if (angle > maxAngle) {
          maxAngle = angle;
        }
      }

      // Normalize against the farthest physical LED so the moving rings span
      // the full rendered topology for any selected axis.
      if (maxAngle <= 1e-9) {
        maxAngle = 1;
      }
      for (int i = 0; i < destination.Length; i++) {
        destination[i] = NormalizeAngularDistance(
          destination[i], maxAngle);
      }
    }

    // Angular distance is monotonic away from the forward pole, so
    // each constant value is a plane/ring perpendicular to the selected axis.
    // Kept internal for a small geometry regression test.
    internal static double AngularDistance(Vector3 pixel, Vector3 axis) {
      return DomeSurfaceGeometry.AngularDistance(pixel, axis);
    }

    internal static double NormalizeAngularDistance(
      double angle, double maxAngle
    ) => DomeSurfaceGeometry.NormalizeAngularDistance(angle, maxAngle);

    private void RenderPixels(int ringCount, int tint, double[] radii) {
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        double radius = radii[i];
        double bestStrength = 0;
        double bestCoverage = 0;

        for (int ring = 0; ring < ringCount; ring++) {
          double halfWidth = this.ringHalfWidths[ring];
          double distance = Math.Abs(radius - this.ringRadii[ring]);
          if (distance >= halfWidth) {
            continue;
          }

          // Smooth the triangular cross-section so thin rings remain clean on
          // the dome's sparse LED lattice instead of popping at hard edges.
          double profile = 1 - distance / halfWidth;
          profile = profile * profile * (3 - 2 * profile);
          double strength = profile * this.ringBrightnesses[ring];
          if (strength > bestStrength) {
            bestStrength = strength;
            bestCoverage = profile;
          }
        }

        if (bestStrength <= 0) {
          continue;
        }
        ref LEDDomeOutputPixel pixel = ref this.buffer.pixels[i];
        pixel.color = LEDColor.ScaleColor(tint, Math.Min(1, bestStrength));
        // Coverage follows the soft ring edge, while brightness variation stays
        // in RGB. A dim ring therefore remains solid at its center under Over,
        // and Add receives the intended scaled light value.
        pixel.SetAlpha(bestCoverage);
      }
    }

    private static double Lerp(double a, double b, double amount) =>
      a + (b - a) * amount;

    private static double Fraction(double value) =>
      value - Math.Floor(value);

    private static uint InstanceSeed(string value) {
      unchecked {
        uint hash = 2166136261u;
        if (value != null) {
          for (int i = 0; i < value.Length; i++) {
            hash ^= value[i];
            hash *= 16777619u;
          }
        }
        return hash;
      }
    }

    private static double Hash01(uint seed, int ring, uint channel) {
      unchecked {
        uint hash = seed
          ^ ((uint)(ring + 1) * 0x9E3779B9u)
          ^ ((channel + 1) * 0x85EBCA6Bu);
        hash ^= hash >> 16;
        hash *= 0x7FEB352Du;
        hash ^= hash >> 15;
        hash *= 0x846CA68Bu;
        hash ^= hash >> 16;
        return (hash & 0x00FFFFFFu) / 16777215.0;
      }
    }
  }
}

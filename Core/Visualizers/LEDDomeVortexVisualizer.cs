using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Spectrum.Visualizers {

  // A particle-looking vortex with no particle simulation. Each physical LED
  // samples a procedural density field in polar coordinates. Differential
  // angular advection makes the inner field rotate faster than the outside;
  // radial advection supplies inward/outward flow, and a periodic value-noise
  // lattice breaks the result into coherent wisps or grains. Runtime is
  // O(pixels), with no per-frame allocation and no cost tied to density.
  class LEDDomeVortexVisualizer : DomeLayerVisualizer {

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private readonly IAudioLevelInput audio;
    private readonly BeatBroadcaster beats;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;

    // Static projected polar geometry. The vortex is intentionally viewed
    // down the dome's axis, so the flat projection is the useful coordinate
    // space here (unlike effects painted over the unit hemisphere surface).
    private readonly double[] radii;
    private readonly double[] angleTurns;
    private readonly double[] angularSpeedFactors;
    private readonly double[] spiralLogRadii;
    private readonly double[] coreMasks;
    private readonly PeriodicNoiseLattice coarseNoise = new();
    private readonly PeriodicNoiseLattice fineNoise = new();
    private readonly FrameClock frameClock = new FrameClock();
    private readonly double maximumRadius;

    private double time;
    private double lastBeatProgress = -1;
    private int previousStyle = -1;
    private double appliedBrightness = 1;
    private double cachedCoreSize = double.NaN;

    // Never flatten the persistent field all the way to zero: keeping a tiny
    // double-precision scale lets the next frame restore the field before it
    // fades and repaints it. The packed wire color is still black at this
    // level, so silence remains visually silent.
    private const double MinimumBrightnessScale = 1e-6;

    public LEDDomeVortexVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      IAudioLevelInput audio,
      BeatBroadcaster beats,
      DomeRenderContext dome
    ) {
      this.environment = environment;
      this.runtime = runtime;
      this.audio = audio;
      this.beats = beats;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();

      int count = this.buffer.pixels.Length;
      this.radii = new double[count];
      this.angleTurns = new double[count];
      this.angularSpeedFactors = new double[count];
      this.spiralLogRadii = new double[count];
      this.coreMasks = new double[count];
      double maxRadius = 0;
      for (int i = 0; i < count; i++) {
        DomeTopologyPixel point = this.buffer.Topology.PixelAt(i);
        double x = point.X * 2 - 1;
        double y = 1 - point.Y * 2;
        this.radii[i] = Math.Sqrt(x * x + y * y);
        maxRadius = Math.Max(maxRadius, this.radii[i]);
        double turns = Math.Atan2(y, x) / (2 * Math.PI);
        this.angleTurns[i] = turns < 0 ? turns + 1 : turns;
      }
      this.maximumRadius = maxRadius;
    }

    public int Priority => 2;

    public string LayerKey => "vortex";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[]? inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.audio });
    }

    public void Visualize() {
      // Audio brightness is applied to this same persistent layer buffer after
      // rendering. Undo the previous frame's scale first so Fade and the field
      // strength comparison continue to operate on the unmodulated trail. This
      // also preserves any global post-frame hue rotation applied in between.
      RestoreFieldBrightness();

      VortexLayerOptions options =
        this.runtime.GetOptions<VortexLayerOptions>();
      int style = options.Style;
      double speed = options.Speed;
      bool audioBrightness = options.AudioBrightness;
      bool beatSpeed = options.BeatSpeed;
      double twist = options.Twist;
      double scale = options.Scale;
      double density = options.Density;
      double coreSize = options.CoreSize;
      double inflow = options.Inflow;
      double turbulence = options.Turbulence;
      int tint = options.Color;
      double audioLevel = AudioResponseLevel(this.audio.Volume);

      double frameScale = this.frameClock.Tick();
      this.time += frameScale / FrameClock.NominalFps;

      // ProgressThroughMeasure wraps from nearly 1 back to 0 at each beat.
      // Advance the shared field clock by one short impulse on that edge so
      // spin, radial inflow, and fine-grain drift all pulse forward together.
      double beatProgress = this.beats.ProgressThroughMeasure;
      this.time += BeatPulseAdvance(
        this.lastBeatProgress, beatProgress, beatSpeed);
      this.lastBeatProgress = beatProgress;

      // Retain the previous field according to the global Fade speed. At zero
      // retention this clears the previous frame; increasing Fade speed keeps
      // progressively longer trails. Clear when switching styles so one
      // rendering contract cannot leave stale pixels in the other.
      if (style != this.previousStyle) {
        ClearBuffer();
        this.previousStyle = style;
      } else {
        double frameRetention =
          1 - Math.Pow(5, -this.environment.GlobalFadeSpeed);
        this.buffer.Fade(Math.Pow(frameRetention, frameScale), 0);
      }

      double tr = (tint >> 16) & 0xFF;
      double tg = (tint >> 8) & 0xFF;
      double tb = tint & 0xFF;

      // An integer angular period makes the noise exactly continuous across
      // atan2's 0/1 seam. About 2*pi angular cells per radial cell keeps grains
      // approximately isotropic near the rim.
      int angularPeriod = Math.Max(4, (int)Math.Round(2 * Math.PI * scale));
      double radialDrift = this.time * inflow * scale * 0.18;
      int fineAngularPeriod = angularPeriod * 2;
      double fineRadialScale = scale * 2;
      double fineRadialOffset = radialDrift * 2
        - this.time * inflow * scale * 0.11;
      this.coarseNoise.Prepare(
        angularPeriod,
        radialDrift,
        this.maximumRadius * scale + radialDrift);
      this.fineNoise.Prepare(
        fineAngularPeriod,
        fineRadialOffset,
        this.maximumRadius * fineRadialScale + fineRadialOffset);
      double fineWeight = 0.35 * turbulence;
      double coarseWeight = 1 - fineWeight;
      double grainThreshold = 0.92 - density * 0.72;
      this.EnsureCoreGeometry(coreSize);

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        double radius = this.radii[i];

        // Inner material rotates faster, producing the characteristic vortex
        // shear. log(r) bends constant-phase lines into logarithmic spirals.
        double advectedTurns = this.angleTurns[i]
          - this.time * speed * this.angularSpeedFactors[i]
          + twist * this.spiralLogRadii[i] * 0.12;
        double angular = advectedTurns * angularPeriod;
        double radial = radius * scale + radialDrift;

        double coarse = this.coarseNoise.Sample(angular, radial);
        double fine = this.fineNoise.Sample(
          angular * 2 + 17.3,
          radius * fineRadialScale + fineRadialOffset
        );
        double noise = coarse * coarseWeight + fine * fineWeight;

        // Smooth dark eye. The transition remains stable at the minimum core
        // size and avoids a hard circular cutout on the low-resolution dome.
        double coreMask = this.coreMasks[i];
        double value;

        if (style == 1) {
          // Thresholded fine structure: apparent particle count changes with
          // density, but evaluation cost does not. Only current bright grains
          // are stamped; Fade above turns their previous positions into trails.
          value = SmoothStep(
            grainThreshold, grainThreshold + 0.12, noise) * coreMask;
          if (value <= 0.02) {
            continue;
          }
        } else {
          // Repeating triangular phase makes broad spiral arms without a sin()
          // per pixel. Noise perturbs their phase and brightness into water-like
          // streamers instead of mathematically perfect bands.
          double armPhase = Fraction(
            advectedTurns * 3 + (coarse - 0.5) * turbulence
          );
          double arm = 1 - Math.Abs(armPhase * 2 - 1);
          arm = SmoothStep(0.18, 0.9, arm);
          value = arm * (0.35 + 0.65 * noise) * coreMask;
        }

        value = Math.Clamp(value, 0, 1);
        ref LEDDomeOutputPixel pixel = ref this.buffer.pixels[i];

        // Paint the current sample only when it is stronger than the faded
        // history at this pixel. This persistence envelope gives Whirlpool's
        // continuous field visible trails instead of overwriting them every
        // frame. It also preserves post-frame global Hue rotation on older
        // trail segments until a brighter, freshly tinted sample replaces
        // them. With Fade speed zero, history alpha is zero and the current
        // field is reproduced without persistence.
        if (value <= pixel.a) {
          continue;
        }
        pixel.color =
          ((int)(tr * value) << 16) |
          ((int)(tg * value) << 8) |
          (int)(tb * value);
        // Coverage follows density, so Over reveals lower layers between wisps
        // instead of treating dim/black field samples as opaque paint.
        pixel.SetAlpha(value);
      }

      if (audioBrightness) {
        ApplyFieldBrightness(Math.Max(
          MinimumBrightnessScale, audioLevel));
      }
    }

    // These radial terms only change with Core Size, but the old render loop
    // recalculated a division, logarithm, and smoothstep for every pixel on
    // every frame. Rebuild the arrays only when that operator setting changes.
    private void EnsureCoreGeometry(double coreSize) {
      if (coreSize == this.cachedCoreSize) {
        return;
      }
      double innerCore = coreSize * 0.45;
      for (int i = 0; i < this.radii.Length; i++) {
        double radius = this.radii[i];
        double safeRadius = coreSize + radius;
        this.angularSpeedFactors[i] = 0.035 + 0.045 / safeRadius;
        this.spiralLogRadii[i] = Math.Log(safeRadius);
        this.coreMasks[i] = SmoothStep(innerCore, coreSize, radius);
      }
      this.cachedCoreSize = coreSize;
    }

    // Expand the quieter half of the peak meter so both audio modes remain
    // expressive at ordinary listening levels while retaining 0..1 bounds.
    internal static double AudioResponseLevel(double level) =>
      Math.Sqrt(Math.Clamp(level, 0, 1));

    // One pulse advances the field by ten nominal frames. The first sample only
    // establishes a baseline, preventing a newly created/enabled layer from
    // jumping before an actual beat boundary passes.
    internal static double BeatPulseAdvance(
      double previousProgress, double currentProgress, bool enabled
    ) => enabled && previousProgress >= 0 &&
      currentProgress < previousProgress
        ? 10 / FrameClock.NominalFps
        : 0;

    private void RestoreFieldBrightness() {
      if (this.appliedBrightness == 1) {
        return;
      }
      ScaleBuffer(1 / this.appliedBrightness);
      this.appliedBrightness = 1;
    }

    private void ApplyFieldBrightness(double scale) {
      ScaleBuffer(scale);
      this.appliedBrightness = scale;
    }

    private void ScaleBuffer(double scale) {
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        ref LEDDomeOutputPixel pixel = ref this.buffer.pixels[i];
        pixel.SetRGB(
          pixel.r * scale, pixel.g * scale, pixel.b * scale);
        pixel.SetAlpha(pixel.a * scale);
      }
    }

    private void ClearBuffer() {
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        this.buffer.pixels[i].Clear();
      }
    }

    // Retained as the exact reference for regression tests. Production prepares
    // the same integer lattice rows once and reuses their hash values across all
    // pixels and subsequent frames.
    internal static double PeriodicValueNoise(
      double x, double y, int period
    ) {
      int x0 = FastFloor(x);
      int y0 = FastFloor(y);
      double fx = SmoothCurve(x - x0);
      double fy = SmoothCurve(y - y0);

      int wx0 = PositiveMod(x0, period);
      int wx1 = wx0 + 1;
      if (wx1 == period) {
        wx1 = 0;
      }

      double a = Lerp(Hash01(wx0, y0), Hash01(wx1, y0), fx);
      double b = Lerp(Hash01(wx0, y0 + 1), Hash01(wx1, y0 + 1), fx);
      return Lerp(a, b, fy);
    }

    // PeriodicValueNoise's hash depends only on integer lattice coordinates.
    // At the default scale thousands of dome pixels repeatedly request a few
    // dozen rows, so cache those rows and leave only interpolation in the pixel
    // loop. A bounded reusable store prevents an endlessly drifting field from
    // retaining rows for the lifetime of the show.
    internal sealed class PeriodicNoiseLattice {
      private const int MaxCachedRows = 256;

      private readonly Dictionary<int, int> rowSlots =
        new Dictionary<int, int>(MaxCachedRows);
      private double[] rowValues = Array.Empty<double>();
      private int[] preparedOffsets = Array.Empty<int>();
      private int period;
      private int nextSlot;
      private int preparedFirstY;
      private int preparedRowCount;

      internal void Prepare(int nextPeriod, double minimumY, double maximumY) {
        if (nextPeriod <= 0 || !double.IsFinite(minimumY) ||
            !double.IsFinite(maximumY)) {
          throw new ArgumentOutOfRangeException(nameof(nextPeriod));
        }
        if (maximumY < minimumY) {
          (minimumY, maximumY) = (maximumY, minimumY);
        }

        int firstY = FastFloor(minimumY);
        // A bilinear sample at floor(maximumY) also reads the following row.
        int lastY = FastFloor(maximumY) + 1;
        int rowCount = lastY - firstY + 1;
        if (rowCount > MaxCachedRows) {
          throw new ArgumentOutOfRangeException(
            nameof(maximumY), "Prepared noise range is too large.");
        }

        if (this.period != nextPeriod) {
          this.period = nextPeriod;
          this.rowValues = new double[MaxCachedRows * nextPeriod];
          this.rowSlots.Clear();
          this.nextSlot = 0;
        }

        int missingRows = 0;
        for (int y = firstY; y <= lastY; y++) {
          if (!this.rowSlots.ContainsKey(y)) {
            missingRows++;
          }
        }
        if (this.nextSlot + missingRows > MaxCachedRows) {
          this.rowSlots.Clear();
          this.nextSlot = 0;
        }

        if (this.preparedOffsets.Length < rowCount) {
          int capacity = 16;
          while (capacity < rowCount) {
            capacity *= 2;
          }
          this.preparedOffsets = new int[capacity];
        }

        for (int row = 0; row < rowCount; row++) {
          int y = firstY + row;
          if (!this.rowSlots.TryGetValue(y, out int slot)) {
            slot = this.nextSlot++;
            this.rowSlots.Add(y, slot);
            int offset = slot * this.period;
            for (int x = 0; x < this.period; x++) {
              this.rowValues[offset + x] = Hash01(x, y);
            }
          }
          this.preparedOffsets[row] = slot * this.period;
        }
        this.preparedFirstY = firstY;
        this.preparedRowCount = rowCount;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal double Sample(double x, double y) {
        int x0 = FastFloor(x);
        int y0 = FastFloor(y);
        int preparedRow = y0 - this.preparedFirstY;
        if ((uint)preparedRow >= (uint)(this.preparedRowCount - 1)) {
          throw new InvalidOperationException(
            "Noise lattice was not prepared for the requested row.");
        }

        double fx = SmoothCurve(x - x0);
        double fy = SmoothCurve(y - y0);
        int wx0 = PositiveMod(x0, this.period);
        int wx1 = wx0 + 1;
        if (wx1 == this.period) {
          wx1 = 0;
        }

        int row0 = this.preparedOffsets[preparedRow];
        int row1 = this.preparedOffsets[preparedRow + 1];
        double a = Lerp(
          this.rowValues[row0 + wx0], this.rowValues[row0 + wx1], fx);
        double b = Lerp(
          this.rowValues[row1 + wx0], this.rowValues[row1 + wx1], fx);
        return Lerp(a, b, fy);
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Hash01(int x, int y) {
      uint h = (uint)(x * 374761393 + y * 668265263);
      h = (h ^ (h >> 13)) * 1274126177u;
      h ^= h >> 16;
      return (h & 0x00FFFFFFu) / 16777215.0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FastFloor(double value) {
      int integer = (int)value;
      return value < integer ? integer - 1 : integer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PositiveMod(int value, int modulus) {
      int result = value % modulus;
      return result < 0 ? result + modulus : result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Fraction(double value) {
      return value - Math.Floor(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SmoothCurve(double value) {
      return value * value * (3 - 2 * value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SmoothStep(double edge0, double edge1, double value) {
      double t = Math.Clamp((value - edge0) / (edge1 - edge0), 0, 1);
      return t * t * (3 - 2 * t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Lerp(double a, double b, double t) {
      return a + (b - a) * t;
    }
  }
}

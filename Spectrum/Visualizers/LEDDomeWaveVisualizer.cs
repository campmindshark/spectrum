using Spectrum.Base;
using Spectrum.LEDs;
using System;

namespace Spectrum.Visualizers {

  // Sweeps a band across the dome. It emits a *mask*, not color: inside the band
  // a pixel is opaque white (alpha 1), outside it is fully transparent (alpha 0,
  // "reveal below"). On its own the layer is degenerate — an adjustment layer
  // needs a layer beneath it. Its point is to drive an adjustment blend (e.g.
  // Desaturate) so the band shows whatever is below it, processed: the band's
  // shape is the wave's job, the desaturation is the blend's (see the
  // two-consumer split in docs/layer_params_implementation.md).
  //
  // Per-layer params (visualizer-consumed, read each frame from this layer's own
  // stack entry): bandWidth, sweep speed, and the axis (dome height vs. angle).
  // Edges are hard by design — no partial alpha.
  class LEDDomeWaveVisualizer : DomeLayerVisualizer {

    private readonly Configuration config;
    private readonly LEDDomeOutput dome;
    private readonly LEDDomeOutputBuffer buffer;

    // Each pixel's position along the two sweep axes, baked once and normalized
    // to 0..1 (periodic): height (1 at the dome's top/center, 0 at the rim) and
    // angle around the dome. The band test picks one per frame from the `axis`
    // param.
    private readonly double[] pixelHeight;
    private readonly double[] pixelAngle;

    // Sweep phase (the band center, 0..1) accumulated from wall-clock time so the
    // sweep speed is frame-rate independent.
    private readonly System.Diagnostics.Stopwatch frameTimer =
      new System.Diagnostics.Stopwatch();
    private double phase;

    public LEDDomeWaveVisualizer(
      Configuration config,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeOutputBuffer();

      // Bake each pixel's normalized height and angle from its strut/LED identity
      // once, exactly like Race, so Visualize allocates nothing per pixel.
      this.pixelHeight = new double[this.buffer.pixels.Length];
      this.pixelAngle = new double[this.buffer.pixels.Length];
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        var pixel = this.buffer.pixels[i];
        var parametric = StrutLayoutFactory.GetProjectedLEDPointParametric(
          pixel.strutIndex,
          pixel.strutLEDIndex
        );
        // Item4 is radial distance (0 center .. ~1 rim); flip so 1 is the top,
        // then clamp into 0..1 for the periodic band test.
        this.pixelHeight[i] = Clamp01(1.0 - parametric.Item4);
        // Item3 is the angle in radians (-pi..pi); map to 0..1.
        this.pixelAngle[i] = (parametric.Item3 / (2 * Math.PI)) + 0.5;
      }
    }

    public int Priority {
      get {
        return DomeLayerSettings.StackActivates(
          this.config.domeLayerStack, "wave"
        ) ? 2 : 0;
      }
    }

    public string LayerKey => "wave";
    public LEDDomeOutputBuffer LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    // Input-free: the band is time-driven, no audio/orientation source.
    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { });
    }

    public void Visualize() {
      var stack = this.config.domeLayerStack;
      double bandWidth =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "bandWidth");
      double speed =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "speed");
      // 0 = Height, 1 = Angle
      bool useAngle =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "axis") >= 0.5;

      // Advance the band center by speed (cycles/second) * elapsed wall time,
      // wrapped into 0..1. Speed may be negative (sweep the other way).
      if (!this.frameTimer.IsRunning) {
        this.frameTimer.Restart();
      } else {
        double elapsed = this.frameTimer.Elapsed.TotalSeconds;
        this.frameTimer.Restart();
        this.phase = Frac(this.phase + speed * elapsed);
      }
      double center = this.phase;

      double[] coords = useAngle ? this.pixelAngle : this.pixelHeight;
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        // Distance to the band center on a 0..1 ring (both axes are periodic).
        double d = Math.Abs(coords[i] - center);
        if (d > 0.5) {
          d = 1 - d;
        }
        if (d < bandWidth) {
          // Opaque white mask: the alpha (1) is what an adjustment blend reads;
          // the color is irrelevant to Desaturate but matters if the layer runs
          // under a color blend, so make it a plain reveal-all white.
          this.buffer.pixels[i].color = 0xFFFFFF;
        } else {
          // Transparent: reveal the layers below unchanged (alpha 0).
          this.buffer.pixels[i].Clear();
        }
      }
    }

    private static double Clamp01(double x) {
      if (x < 0) return 0;
      if (x > 1) return 1;
      return x;
    }

    // Positive fractional part, so a wrapped phase/coordinate stays in [0,1).
    private static double Frac(double x) {
      double f = x - Math.Floor(x);
      return f;
    }
  }
}

using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Immutable;
using System.Numerics;

namespace Spectrum.Visualizers {

  // The signed sibling of the orientation Metaball layer. Every wand is a
  // dipole whose calibrated forward end (OrientationCenter.Spot) is a +1 point
  // charge and whose antipode is -1. OrientationCenter.SignedPotentialAt
  // superposes their +1/r and -1/r potentials, so like charges reinforce,
  // opposite charges cancel, and the field changes sign across its neutral
  // boundaries instead of identifying a wand's two ends as Metaball does.
  //
  // Positive and negative potential use independent colors. Raw point-charge
  // potential is singular at a pole, so the display applies an exponential
  // compression: it is linear near zero, asymptotically reaches full brightness
  // near a charge, and never needs an arbitrary hard cutoff. Coverage follows
  // that same magnitude, leaving cancellation boundaries transparent to layers
  // below when this renderer is composed with Over. A white streamline overlay
  // samples the dipoles' exact surface-flow meridians: in the flat simulator
  // projection they appear as the familiar curved paths joining the two poles.
  class LEDDomeMagneticFieldVisualizer : DomeLayerVisualizer {

    private const double IDLE_LEVEL = 0.4;

    private readonly LayerRendererRuntime runtime;
    private readonly OrientationInput orientationInput;
    private readonly OrientationCenter center;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly ImmutableArray<Vector3> pixelPositions;

    public LEDDomeMagneticFieldVisualizer(
      LayerRendererRuntime runtime,
      OrientationInput orientationInput,
      OrientationCenter center,
      DomeRenderContext dome
    ) {
      this.runtime = runtime;
      this.orientationInput = orientationInput;
      this.center = center;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.pixelPositions = this.buffer.BakePixelPositions();
    }

    public int Priority => 2;

    public string LayerKey => "magnetic-field";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ??
        (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      MagneticFieldLayerOptions options =
        this.runtime.GetOptions<MagneticFieldLayerOptions>();

      // Keep this layer useful without an audio input. The fixed level only
      // controls OrientationCenter's idle wander; live wands ignore it.
      this.center.Update(IDLE_LEVEL);

      int positive = options.PositiveColor;
      int negative = options.NegativeColor;
      int lineCount = options.LineCount;
      double lineWidth = options.LineWidth;
      double positiveHue = new Color(positive).H;
      double negativeHue = new Color(negative).H;

      int positiveR = (positive >> 16) & 0xFF;
      int positiveG = (positive >> 8) & 0xFF;
      int positiveB = positive & 0xFF;
      int negativeR = (negative >> 16) & 0xFF;
      int negativeG = (negative >> 8) & 0xFF;
      int negativeB = negative & 0xFF;

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        Vector3 position = this.pixelPositions[i];
        double potential = this.center.SignedPotentialAt(position);
        double magnitude = 1 - Math.Exp(
          -options.Strength * Math.Abs(potential));
        double lineStrength = this.center.FieldLineStrengthAt(
          position, lineCount, lineWidth);

        bool isPositive = potential >= 0;
        int r = isPositive ? positiveR : negativeR;
        int g = isPositive ? positiveG : negativeG;
        int b = isPositive ? positiveB : negativeB;

        // Potential supplies the signed red/blue body of the field. Blend each
        // streamline most of the way toward white so it stays legible through
        // both charge lobes without erasing their sign entirely.
        double baseR = r * magnitude;
        double baseG = g * magnitude;
        double baseB = b * magnitude;
        double lineMix = 0.8 * lineStrength;

        ref LEDDomeOutputPixel pixel = ref this.buffer.pixels[i];
        pixel.color =
          ((int)(baseR + (255 - baseR) * lineMix) << 16) |
          ((int)(baseG + (255 - baseG) * lineMix) << 8) |
          (int)(baseB + (255 - baseB) * lineMix);
        pixel.SetAlpha(Math.Max(magnitude, lineStrength));
        pixel.hue = isPositive ? positiveHue : negativeHue;
      }
    }
  }
}

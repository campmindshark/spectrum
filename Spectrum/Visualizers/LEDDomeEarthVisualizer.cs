using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Numerics;

namespace Spectrum.Visualizers {

  // Wraps one equirectangular Earth texture over the dome's real unit-sphere
  // pixel positions. OrientationCenter supplies the same spotlighted/first
  // moving wand used by the other orientation layers, or its wandering idle
  // center when no wand is moving. In that orientation's local frame Spot is
  // the north pole and NegSpot is the south pole; longitude advances around
  // their axis at spinSpeed revolutions per second.
  //
  // The dome topology only contains the physical upper hemisphere (z >= 0), so
  // sampling those positions naturally renders the visible half of the globe.
  // There is no screen-space circular mask and no synthetic lower hemisphere.
  class LEDDomeEarthVisualizer : DomeLayerVisualizer {

    internal const string TextureResourceName =
      "Spectrum.Resources.EarthTexture.png";
    private const double IDLE_LEVEL = 0.4;

    private static readonly Lazy<EarthTexture> texture =
      new Lazy<EarthTexture>(EarthTexture.Load);

    private readonly LayerRendererRuntime runtime;
    private readonly OrientationInput orientationInput;
    private readonly OrientationCenter center;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly ImmutableArray<Vector3> pixelPositions;
    private readonly FrameClock frameClock = new FrameClock();

    private double spinTurns;

    public LEDDomeEarthVisualizer(
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

      // Decode once when the first Earth renderer is constructed. Every Earth
      // layer instance shares the immutable RGB bytes through the static Lazy.
      _ = texture.Value;
    }

    public int Priority => 2;

    public string LayerKey => "earth";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ??
        (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      EarthLayerOptions options =
        this.runtime.GetOptions<EarthLayerOptions>();

      double elapsedSeconds = this.frameClock.Tick() / FrameClock.NominalFps;
      this.spinTurns += options.SpinSpeed * elapsedSeconds;
      this.spinTurns -= Math.Floor(this.spinTurns);

      // This is the shared resolution contract requested by the layer: a
      // configured moving spotlight wins, otherwise the first moving sensor,
      // otherwise the idle center. The fixed level controls only idle wander.
      this.center.Update(IDLE_LEVEL);
      Quaternion orientation = this.center.CurrentCenter;
      EarthTexture source = texture.Value;

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        TextureCoordinates(
          this.pixelPositions[i], orientation, this.spinTurns,
          out double u, out double v);
        this.buffer.pixels[i].color = source.Sample(u, v);
      }
    }

    // Converts a physical dome point into the equirectangular texture. Applying
    // the spotlight orientation moves the point into sensor-local coordinates,
    // where OrientationCenter.Spot (-X) is the north pole. +Z is the zero
    // meridian and +Y is east. Subtracting spinTurns moves longitude while
    // leaving latitude (and therefore the sensor-aligned poles) unchanged.
    internal static void TextureCoordinates(
      Vector3 pixelPoint, Quaternion orientation, double spinTurns,
      out double u, out double v
    ) {
      Vector3 local = Vector3.Transform(pixelPoint, orientation);
      double north = Math.Clamp(
        Vector3.Dot(local, OrientationCenter.Spot), -1, 1);
      double longitude = Math.Atan2(local.Y, local.Z);

      u = 0.5 + longitude / (2 * Math.PI) - spinTurns;
      u -= Math.Floor(u);
      v = 0.5 - Math.Asin(north) / Math.PI;
      v = Math.Clamp(v, 0, 1);
    }

    // Immutable decoded texture with bilinear filtering. U wraps so the date
    // line is seamless; V clamps at the polar rows.
    private sealed class EarthTexture {
      private readonly int width;
      private readonly int height;
      private readonly byte[] rgb;

      private EarthTexture(int width, int height, byte[] rgb) {
        this.width = width;
        this.height = height;
        this.rgb = rgb;
      }

      public static EarthTexture Load() {
        using Stream stream = typeof(LEDDomeEarthVisualizer).Assembly
          .GetManifestResourceStream(TextureResourceName)
          ?? throw new InvalidOperationException(
            "Missing embedded Earth texture " + TextureResourceName + ".");
        PortablePngImage source = PortablePngImage.Load(stream);
        return new EarthTexture(source.Width, source.Height, source.Rgb);
      }

      public int Sample(double u, double v) {
        double x = u * this.width - 0.5;
        double y = v * this.height - 0.5;
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        double fx = x - x0;
        double fy = y - y0;
        int x1 = Wrap(x0 + 1, this.width);
        x0 = Wrap(x0, this.width);
        int y1 = Math.Clamp(y0 + 1, 0, this.height - 1);
        y0 = Math.Clamp(y0, 0, this.height - 1);

        int r = InterpolateChannel(x0, x1, y0, y1, fx, fy, 0);
        int g = InterpolateChannel(x0, x1, y0, y1, fx, fy, 1);
        int b = InterpolateChannel(x0, x1, y0, y1, fx, fy, 2);
        return (r << 16) | (g << 8) | b;
      }

      private int InterpolateChannel(
        int x0, int x1, int y0, int y1,
        double fx, double fy, int channel
      ) {
        double top = this.rgb[(y0 * this.width + x0) * 3 + channel] *
          (1 - fx) +
          this.rgb[(y0 * this.width + x1) * 3 + channel] * fx;
        double bottom = this.rgb[(y1 * this.width + x0) * 3 + channel] *
          (1 - fx) +
          this.rgb[(y1 * this.width + x1) * 3 + channel] * fx;
        return (int)Math.Round(top * (1 - fy) + bottom * fy);
      }

      private static int Wrap(int value, int modulus) {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
      }
    }
  }
}

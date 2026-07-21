using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;

namespace Spectrum.Visualizers {

  // A seamless analytic elevation field lives directly on the unit dome.
  // Its broad plane waves establish continents, a fine ridged component adds
  // basins, and one soft summit keeps the terrain composition legible. The
  // field evolves by phase rather than by translating a flat texture, so it
  // has no projection seam or preferred map edge.
  //
  // Interval contours and the current coastline carry most of the light.
  // Subdued land and water fills keep those lines readable when the renderer
  // is used alone. Capture volume raises the configured quiet sea level, and
  // orientation binding rotates the landscape through the shared center.
  class LEDDomeTopographicDreamVisualizer : DomeLayerVisualizer {
    private static readonly Vector3 ContinentalAxisA = Vector3.Normalize(
      new Vector3(0.73f, -0.31f, 0.61f));
    private static readonly Vector3 ContinentalAxisB = Vector3.Normalize(
      new Vector3(-0.24f, 0.91f, 0.34f));
    private static readonly Vector3 ContinentalAxisC = Vector3.Normalize(
      new Vector3(0.52f, 0.47f, -0.71f));
    private static readonly Vector3 ContinentalAxisD = Vector3.Normalize(
      new Vector3(-0.82f, -0.36f, 0.44f));
    private static readonly Vector3 SummitAxis = Vector3.Normalize(
      new Vector3(0.62f, -0.18f, 0.76f));

    private readonly LayerRendererRuntime runtime;
    private readonly AudioInput audio;
    private readonly OrientationInput orientation;
    private readonly OrientationCenter orientationCenter;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly ImmutableArray<Vector3> positions;
    private readonly Stopwatch frameTimer = new Stopwatch();
    private double time;

    public LEDDomeTopographicDreamVisualizer(
      LayerRendererRuntime runtime,
      AudioInput audio,
      OrientationInput orientation,
      OrientationCenter orientationCenter,
      DomeRenderContext dome
    ) {
      this.runtime = runtime;
      this.audio = audio;
      this.orientation = orientation;
      this.orientationCenter = orientationCenter;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.positions = this.buffer.BakePixelPositions();
    }

    public int Priority => 2;
    public string LayerKey => "topographic-dream";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() =>
      this.inputs ?? (this.inputs = new Input[] {
        this.audio, this.orientation,
      });

    public void Visualize() {
      TopographicDreamLayerOptions options =
        this.runtime.GetOptions<TopographicDreamLayerOptions>();
      double elapsed = this.ElapsedSeconds();
      this.time += elapsed * options.EvolutionSpeed;
      double seaLevel = EffectiveSeaLevel(
        options.SeaLevel, this.audio.Volume);

      Quaternion rotation = Quaternion.Identity;
      if (options.BindToOrientation) {
        this.orientationCenter.Update(0.3);
        rotation = this.orientationCenter.CurrentCenter;
      }

      for (int index = 0; index < this.buffer.pixels.Length; index++) {
        Vector3 position = this.positions[index];
        if (options.BindToOrientation) {
          position = Vector3.Transform(position, rotation);
        }
        double elevation = ElevationField(
          position, options.TerrainScale, this.time);
        double contour = ContourStrength(
          elevation, options.ContourInterval, options.LineWidth);
        double coast = CoastlineStrength(
          elevation, seaLevel,
          options.ContourInterval * options.LineWidth * 1.35);
        bool land = elevation >= seaLevel;
        double relativeHeight = land
          ? (elevation - seaLevel) / Math.Max(1 - seaLevel, 0.0001)
          : elevation / Math.Max(seaLevel, 0.0001);
        relativeHeight = Math.Clamp(relativeHeight, 0, 1);

        double fill = land
          ? 0.10 + 0.13 * relativeHeight
          : 0.045 + 0.055 * relativeHeight;
        double line = Math.Max(0.76 * contour, coast);
        double luminance = Math.Min(1, fill + 0.88 * line);
        double palettePosition = coast > contour
          ? 0.98
          : land
            ? 0.34 + 0.62 * relativeHeight
            : 0.04 + 0.18 * relativeHeight;
        int tint = this.dome.GetGradientBetweenColors(
          0, 7, palettePosition, 0, true, options.Palette);

        int r = (int)(((tint >> 16) & 0xFF) * luminance);
        int g = (int)(((tint >> 8) & 0xFF) * luminance);
        int b = (int)((tint & 0xFF) * luminance);
        ref LEDDomeOutputPixel pixel = ref this.buffer.pixels[index];
        pixel.color = (r << 16) | (g << 8) | b;
        pixel.SetAlpha(Math.Min(1, 0.12 + 0.78 * line));
        pixel.hue = palettePosition;
      }
    }

    private double ElapsedSeconds() {
      if (!this.frameTimer.IsRunning) {
        this.frameTimer.Restart();
        return 0;
      }
      double elapsed = this.frameTimer.Elapsed.TotalSeconds;
      this.frameTimer.Restart();
      return Math.Clamp(elapsed, 0, 0.1);
    }

    internal static double EffectiveSeaLevel(
      double quietSeaLevel, double audioLevel
    ) => Math.Clamp(
      quietSeaLevel + 0.28 * Math.Sqrt(Math.Clamp(audioLevel, 0, 1)),
      0, 1);

    // The result is normalized to [0,1]. Sampling a 3D direction rather than
    // projected coordinates makes the field continuous across all azimuths.
    internal static double ElevationField(
      Vector3 position, double terrainScale, double time
    ) {
      Vector3 p = position.LengthSquared() > 1e-12
        ? Vector3.Normalize(position)
        : Vector3.UnitZ;
      double scale = Math.Max(0.1, terrainScale);
      double broad =
        0.48 * Math.Sin(
          scale * 2.25 * Vector3.Dot(p, ContinentalAxisA) + 0.37 * time) +
        0.27 * Math.Sin(
          scale * 3.45 * Vector3.Dot(p, ContinentalAxisB) - 0.23 * time + 1.7) +
        0.16 * Math.Sin(
          scale * 5.8 * Vector3.Dot(p, ContinentalAxisC) + 0.51 * time + 3.1) +
        0.09 * Math.Sin(
          scale * 9.2 * Vector3.Dot(p, ContinentalAxisD) - 0.68 * time + 0.4);
      double ridgeWave = Math.Sin(
        scale * 7.1 * Vector3.Dot(p, ContinentalAxisA + ContinentalAxisC) +
        0.41 * time + 2.2);
      double ridge = 1 - 2 * Math.Abs(ridgeWave);
      double summit = Math.Exp(
        7.5 * (Vector3.Dot(p, SummitAxis) - 1));
      return Math.Clamp(
        0.45 + 0.32 * broad + 0.10 * ridge + 0.18 * summit,
        0, 1);
    }

    internal static double ContourStrength(
      double elevation, double interval, double lineWidth
    ) {
      interval = Math.Max(0.001, interval);
      double phase = elevation / interval;
      double distance = Math.Abs(phase - Math.Round(phase));
      return SmoothBand(distance, Math.Clamp(lineWidth, 0.001, 0.499));
    }

    internal static double CoastlineStrength(
      double elevation, double seaLevel, double halfWidth
    ) => SmoothBand(
      Math.Abs(elevation - seaLevel), Math.Max(0.0001, halfWidth));

    private static double SmoothBand(double distance, double halfWidth) {
      double strength = 1 - distance / halfWidth;
      if (strength <= 0) {
        return 0;
      }
      strength = Math.Min(1, strength);
      return strength * strength * (3 - 2 * strength);
    }
  }
}

namespace Spectrum.Base {

public sealed record EmptyLayerRendererOptions : ILayerRendererOptions {
    public static EmptyLayerRendererOptions Instance { get; } = new();
  }

  public sealed record RadialLayerOptions(
    int Effect, double Size, double Frequency, double CenterAngle,
    double CenterDistance, double CenterSpeed, double RotationSpeed,
    double GradientSpeed, int Palette
  ) : ILayerRendererOptions;

  public sealed record VolumeLayerOptions(
    int AnimationSize, double RotationSpeed, double GradientSpeed, int Palette
  ) : ILayerRendererOptions;

  public sealed record RaceLayerOptions(
    double Speed, double Spacing, int Palette
  ) : ILayerRendererOptions;

  public sealed record PaletteLayerOptions(int Palette) : ILayerRendererOptions;

  public sealed record TwinkleLayerOptions(double Density)
    : ILayerRendererOptions;

  public sealed record PaintbrushLayerOptions(
    double Size, double TwinkleDensity, double RippleCooldown,
    double RippleSpeed
  ) : ILayerRendererOptions;

  public sealed record RippleLayerOptions(
    double RippleSpeed, double Desaturation, int Trigger, int Button,
    double Level, double Interval
  ) : ILayerRendererOptions;

  public sealed record StampLayerOptions(
    int Trigger, int Button, double Level, double Interval
  ) : ILayerRendererOptions;

  public sealed record TunnelLayerOptions(
    int RingCount, double Speed, double Thickness, double Brightness,
    double Variation, bool BindToOrientation, int Color
  ) : ILayerRendererOptions;

  public sealed record MetaballLayerOptions(
    double Size, bool ShowContours, int Button
  ) : ILayerRendererOptions;

  public sealed record MagneticFieldLayerOptions(
    double Strength, int PositiveColor, int NegativeColor,
    int LineCount, double LineWidth
  ) : ILayerRendererOptions;

  public sealed record BackgroundLayerOptions(int Color)
    : ILayerRendererOptions;

  public sealed record EarthLayerOptions(double SpinSpeed)
    : ILayerRendererOptions;

  public sealed record AstronomyLayerOptions(
    double NorthHeading, int StartDate, double TimeOffsetHours,
    bool ShowDaytimeSky, bool ShowNighttimeSky,
    double PlaybackSpeed, bool Loop
  ) : ILayerRendererOptions;

  public sealed record FlashLayerOptions(
    int Color, int Trigger, int Button, double Level, double Interval
  ) : ILayerRendererOptions;

  public sealed record WaveLayerOptions(
    double BandWidth, double Speed, double CenterAngle, double CenterDistance,
    int Color, bool OneShot, int Button
  ) : ILayerRendererOptions;

  public sealed record PointCloudLayerOptions(
    int Count, double SpotSize, double PushRadius, double PushStrength,
    double SpringStrength, double Damping
  ) : ILayerRendererOptions;

  public sealed record ShootingStarLayerOptions(
    double SpawnRate, double Acceleration, double MaxSpeed, double Size,
    bool Homing, int Trigger, int Button, double Level, double Interval,
    int Palette
  ) : ILayerRendererOptions;

  public sealed record SparklerLayerOptions(
    double EmissionRate, double Speed, double Size, int Trigger, int Button,
    double Level, double Interval, int Palette
  ) : ILayerRendererOptions;

  public sealed record GyroscopeLayerOptions(
    double RingWidth, double RotorRate, int Palette
  ) : ILayerRendererOptions;

  public sealed record WatchfulIrisLayerOptions(
    int IrisComplexity, double PupilSize, double DilationGain,
    int BlinkTrigger, double EyelidSoftness, double ScleraBrightness,
    int Palette
  ) : ILayerRendererOptions;

  public sealed record LivingSkinLayerOptions(
    double FeedRate, double KillRate, double DiffusionScale,
    double SimulationSpeed, int SeedSource, double EdgeContrast,
    int FeedButton, int PoisonButton, int EraseButton,
    int BrushRadius, double BrushStrength, int Palette
  ) : ILayerRendererOptions;

  public sealed record ArcLightningLayerOptions(
    int BranchCount, double Jaggedness, int Width,
    double Afterglow, double Duration,
    int Trigger, int Button, double Level, double Interval,
    int Palette
  ) : ILayerRendererOptions;

  public sealed record GlassMosaicLayerOptions(
    int TileGrouping, double CascadeSpeed, int PropagationRule,
    double BorderBrightness, int TileTransition, int Trigger, int Button,
    double Level, double Interval, int Palette
  ) : ILayerRendererOptions;

  public sealed record CellularDomeLayerOptions(
    int Rule, int Neighborhood, double GenerationRate,
    int BirthColor, double AgeDecay, int TriggerMode, int Palette
  ) : ILayerRendererOptions;

  public sealed record FireflySwarmLayerOptions(
    int Population, double Cohesion, double Separation, double Wander,
    int InteractionMode, double DotSize, double TrailLength, int Palette
  ) : ILayerRendererOptions;

  public sealed record RainChamberLayerOptions(
    double RainfallRate, double Gravity, double DropletSize,
    double TrailRetention, int InteractionMode, double Wind,
    double SplashStrength, int Palette
  ) : ILayerRendererOptions;

  public sealed record TopographicDreamLayerOptions(
    double TerrainScale, double EvolutionSpeed, double ContourInterval,
    double LineWidth, double SeaLevel, bool BindToOrientation, int Palette
  ) : ILayerRendererOptions;

  public sealed record OrbitalGardenLayerOptions(
    int BodyCount, double Gravity, double OrbitalDamping,
    int CollisionBehavior, double TrailLength, double BodySize, int Palette
  ) : ILayerRendererOptions;

  public sealed record LavaLampSkyLayerOptions(
    int BlobCount, double Viscosity, double Buoyancy,
    double SurfaceTension, double Heat, bool BindGravity, int Palette
  ) : ILayerRendererOptions;

  public sealed record NoiseCloudLayerOptions(
    double Scale, double Speed, int Octaves, double Contrast, int Color
  ) : ILayerRendererOptions;

  public sealed record VortexLayerOptions(
    int Style, double Speed, bool AudioBrightness, bool BeatSpeed,
    double Twist, double Scale, double Density, double CoreSize,
    double Inflow, double Turbulence, int Color
  ) : ILayerRendererOptions;

  public sealed record CausticsLayerOptions(
    int Method, double Scale, double Speed, double Sharpness,
    double Brightness, int Color
  ) : ILayerRendererOptions;

  public sealed record RippleTankLayerOptions(
    double Speed, double Damping, double Sharpness, double Brightness,
    int Color
  ) : ILayerRendererOptions;

}

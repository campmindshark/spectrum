using System;
using System.Collections.Immutable;

namespace Spectrum.Base {

  // Marker for immutable renderer options compiled from the serializer-facing
  // parameter bag. Renderers consume the concrete records below; string keys
  // are confined to the stack-compilation boundary.
  public interface ILayerRendererOptions { }

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
    double RippleSpeed, int Trigger, int Button, double Level, double Interval
  ) : ILayerRendererOptions;

  public sealed record StampLayerOptions(
    int Trigger, int Button, double Level, double Interval
  ) : ILayerRendererOptions;

  public sealed record MetaballLayerOptions(
    double Size, bool ShowContours, int Button
  ) : ILayerRendererOptions;

  public sealed record BackgroundLayerOptions(int Color)
    : ILayerRendererOptions;

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
    double Speed, double Size, int Trigger, int Button, double Level,
    double Interval, int Palette
  ) : ILayerRendererOptions;

  public sealed record GyroscopeLayerOptions(
    double RingWidth, double RotorRate, int Palette
  ) : ILayerRendererOptions;

  public sealed record NoiseCloudLayerOptions(
    double Scale, double Speed, int Octaves, double Contrast, int Color
  ) : ILayerRendererOptions;

  public sealed record VortexLayerOptions(
    int Style, double Speed, double Twist, double Scale, double Density,
    double CoreSize, double Inflow, double Turbulence, int Color
  ) : ILayerRendererOptions;

  public sealed record CausticsLayerOptions(
    int Method, double Scale, double Speed, double Sharpness,
    double Brightness, int Color
  ) : ILayerRendererOptions;

  public sealed record RippleTankLayerOptions(
    double Speed, double Damping, double Sharpness, double Brightness,
    int Color
  ) : ILayerRendererOptions;

  internal static class LayerRendererOptionsCompiler {
    private static ParameterValue Value(
      ImmutableDictionary<string, ParameterValue> values, string key
    ) {
      if (values != null && values.TryGetValue(key, out ParameterValue value)) {
        return value;
      }
      throw new InvalidOperationException(
        "Compiled renderer options are missing parameter " + key + ".");
    }

    private static double Double(
      ImmutableDictionary<string, ParameterValue> values, string key
    ) => Value(values, key).Value;

    private static int Integer(
      ImmutableDictionary<string, ParameterValue> values, string key
    ) => Value(values, key).AsInteger;

    // A few historical numeric sliders feed integer algorithms even though
    // their persisted schema is Double. Preserve their old C# cast semantics
    // instead of changing fractional values from truncation to rounding.
    private static int TruncatedInteger(
      ImmutableDictionary<string, ParameterValue> values, string key
    ) => (int)Value(values, key).Value;

    private static bool Boolean(
      ImmutableDictionary<string, ParameterValue> values, string key
    ) => Value(values, key).AsBoolean;

    internal static ILayerRendererOptions Empty(
      ImmutableDictionary<string, ParameterValue> values
    ) => EmptyLayerRendererOptions.Instance;

    internal static ILayerRendererOptions Radial(
      ImmutableDictionary<string, ParameterValue> v
    ) => new RadialLayerOptions(
      Integer(v, "effect"), Double(v, "size"), Double(v, "frequency"),
      Double(v, "centerAngle"), Double(v, "centerDistance"),
      Double(v, "centerSpeed"), Double(v, "rotationSpeed"),
      Double(v, "gradientSpeed"), Integer(v, "palette"));

    internal static ILayerRendererOptions Volume(
      ImmutableDictionary<string, ParameterValue> v
    ) => new VolumeLayerOptions(
      TruncatedInteger(v, "animationSize"), Double(v, "rotationSpeed"),
      Double(v, "gradientSpeed"), Integer(v, "palette"));

    internal static ILayerRendererOptions Race(
      ImmutableDictionary<string, ParameterValue> v
    ) => new RaceLayerOptions(
      Double(v, "speed"), Double(v, "spacing"), Integer(v, "palette"));

    internal static ILayerRendererOptions Palette(
      ImmutableDictionary<string, ParameterValue> v
    ) => new PaletteLayerOptions(Integer(v, "palette"));

    internal static ILayerRendererOptions Twinkle(
      ImmutableDictionary<string, ParameterValue> v
    ) => new TwinkleLayerOptions(Double(v, "density"));

    internal static ILayerRendererOptions Paintbrush(
      ImmutableDictionary<string, ParameterValue> v
    ) => new PaintbrushLayerOptions(
      Double(v, "size"), Double(v, "twinkleDensity"),
      Double(v, "rippleCDStep"), Double(v, "rippleStep"));

    internal static ILayerRendererOptions Ripple(
      ImmutableDictionary<string, ParameterValue> v
    ) => new RippleLayerOptions(
      Double(v, "rippleStep"), Integer(v, "trigger"), Integer(v, "button"),
      Double(v, "level"), Double(v, "interval"));

    internal static ILayerRendererOptions Stamp(
      ImmutableDictionary<string, ParameterValue> v
    ) => new StampLayerOptions(
      Integer(v, "trigger"), Integer(v, "button"), Double(v, "level"),
      Double(v, "interval"));

    internal static ILayerRendererOptions Metaball(
      ImmutableDictionary<string, ParameterValue> v
    ) => new MetaballLayerOptions(
      Double(v, "size"), Boolean(v, "contours"), Integer(v, "button"));

    internal static ILayerRendererOptions Background(
      ImmutableDictionary<string, ParameterValue> v
    ) => new BackgroundLayerOptions(Integer(v, "color"));

    internal static ILayerRendererOptions Flash(
      ImmutableDictionary<string, ParameterValue> v
    ) => new FlashLayerOptions(
      Integer(v, "color"), Integer(v, "trigger"), Integer(v, "button"),
      Double(v, "level"), Double(v, "interval"));

    internal static ILayerRendererOptions Wave(
      ImmutableDictionary<string, ParameterValue> v
    ) => new WaveLayerOptions(
      Double(v, "bandWidth"), Double(v, "speed"), Double(v, "centerAngle"),
      Double(v, "centerDistance"), Integer(v, "color"),
      Integer(v, "mode") == 1, Integer(v, "button"));

    internal static ILayerRendererOptions PointCloud(
      ImmutableDictionary<string, ParameterValue> v
    ) => new PointCloudLayerOptions(
      TruncatedInteger(v, "count"), Double(v, "spotSize"),
      Double(v, "pushRadius"), Double(v, "pushStrength"),
      Double(v, "springStrength"), Double(v, "damping"));

    internal static ILayerRendererOptions ShootingStar(
      ImmutableDictionary<string, ParameterValue> v
    ) => new ShootingStarLayerOptions(
      Double(v, "spawnRate"), Double(v, "accel"), Double(v, "maxSpeed"),
      Double(v, "size"), Boolean(v, "homing"), Integer(v, "trigger"),
      Integer(v, "button"), Double(v, "level"), Double(v, "interval"),
      Integer(v, "palette"));

    internal static ILayerRendererOptions Sparkler(
      ImmutableDictionary<string, ParameterValue> v
    ) => new SparklerLayerOptions(
      Double(v, "speed"), Double(v, "size"), Integer(v, "trigger"),
      Integer(v, "button"), Double(v, "level"), Double(v, "interval"),
      Integer(v, "palette"));

    internal static ILayerRendererOptions Gyroscope(
      ImmutableDictionary<string, ParameterValue> v
    ) => new GyroscopeLayerOptions(
      Double(v, "ringWidth"), Double(v, "rotorRate"),
      Integer(v, "palette"));

    internal static ILayerRendererOptions NoiseCloud(
      ImmutableDictionary<string, ParameterValue> v
    ) => new NoiseCloudLayerOptions(
      Double(v, "scale"), Double(v, "speed"),
      TruncatedInteger(v, "octaves"), Double(v, "contrast"),
      Integer(v, "color"));

    internal static ILayerRendererOptions Vortex(
      ImmutableDictionary<string, ParameterValue> v
    ) => new VortexLayerOptions(
      Integer(v, "style"), Double(v, "speed"), Double(v, "twist"),
      Double(v, "scale"), Double(v, "density"), Double(v, "coreSize"),
      Double(v, "inflow"), Double(v, "turbulence"), Integer(v, "color"));

    internal static ILayerRendererOptions Caustics(
      ImmutableDictionary<string, ParameterValue> v
    ) => new CausticsLayerOptions(
      Integer(v, "method"), Double(v, "scale"), Double(v, "speed"),
      Double(v, "sharpness"), Double(v, "brightness"), Integer(v, "color"));

    internal static ILayerRendererOptions RippleTank(
      ImmutableDictionary<string, ParameterValue> v
    ) => new RippleTankLayerOptions(
      Double(v, "speed"), Double(v, "damping"), Double(v, "sharpness"),
      Double(v, "brightness"), Integer(v, "color"));
  }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spectrum.Base {

  // The value type of a per-layer parameter. Values live in the bag as double
  // regardless: Bool is 0/1, Enum is the index into DomeLayerParam.Options,
  // Color is a packed 0xRRGGBB int and Date is yyyyMMdd, both reinterpreted as
  // doubles so existing serializer-facing parameter bags stay compatible.
  public enum DomeLayerParamType { Double, Bool, Enum, Color, Date }

  // Static schema for one tunable on a layer (or on a blend mode). The bag on a
  // DomeLayerSettings stores only values keyed by DomeLayerParam.Key; everything
  // else here (range, label, default, which consumer reads it) is compile-time
  // metadata read identically by both UIs and the snapshot compiler.
  // See LayerCatalog for visualizer schemas and DomeBlend.Params for the
  // per-blend schemas.
  public sealed class DomeLayerParam {
    public string Key { get; set; }              // unique within the visualizer
    public string Label { get; set; }            // shown in both UIs
    public DomeLayerParamType Type { get; set; }
    public double Min { get; set; }              // Double sliders
    public double Max { get; set; }
    public double Step { get; set; }
    public string[] Options { get; set; }        // Enum labels (index == value)
    public double Default { get; set; }
    // Date params may use Default = 0 to mean today's date in this zone. The
    // dynamic value is resolved before it reaches either UI or the renderer.
    public string TimeZoneId { get; set; }
    // true => read by the compositor (CompositeBlend) once per frame, never by
    // the visualizer. false => read by the visualizer in Visualize().
    public bool CompositorConsumed { get; set; }
  }

  public static class DomeLayerDate {
    public const string PacificTimeZoneId = "Pacific Standard Time";
    private const string PacificIanaTimeZoneId = "America/Los_Angeles";

    public static double ResolveDefault(
      DomeLayerParam descriptor, DateTime? utcNow = null
    ) {
      if (descriptor.Type != DomeLayerParamType.Date ||
          descriptor.Default != 0) {
        return descriptor.Default;
      }
      return CurrentDate(
        utcNow ?? DateTime.UtcNow, descriptor.TimeZoneId);
    }

    public static int CurrentDate(DateTime utc, string timeZoneId) {
      DateTime normalizedUtc = utc.Kind == DateTimeKind.Utc
        ? utc : utc.ToUniversalTime();
      DateTime local = TimeZoneInfo.ConvertTimeFromUtc(
        normalizedUtc, FindTimeZone(timeZoneId));
      return Encode(local);
    }

    public static int Encode(DateTime date) =>
      date.Year * 10000 + date.Month * 100 + date.Day;

    public static bool TryDecode(double value, out DateTime date) {
      date = default;
      if (double.IsNaN(value) || double.IsInfinity(value)) {
        return false;
      }
      double rounded = Math.Round(value);
      if (Math.Abs(value - rounded) > 1e-9 ||
          rounded < 10101 || rounded > 99991231) {
        return false;
      }
      int encoded = (int)rounded;
      int year = encoded / 10000;
      int month = encoded / 100 % 100;
      int day = encoded % 100;
      try {
        date = new DateTime(year, month, day, 0, 0, 0,
          DateTimeKind.Unspecified);
        return Encode(date) == encoded;
      } catch (ArgumentOutOfRangeException) {
        return false;
      }
    }

    public static bool TryParse(string text, out double value) {
      value = 0;
      if (!DateTime.TryParseExact(
          text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
          DateTimeStyles.None, out DateTime date)) {
        return false;
      }
      value = Encode(date);
      return true;
    }

    public static string Format(double value) =>
      TryDecode(value, out DateTime date)
        ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : string.Empty;

    public static DateTime MidnightUtc(double value, string timeZoneId) {
      if (!TryDecode(value, out DateTime date)) {
        throw new ArgumentOutOfRangeException(
          nameof(value), "Date values must use yyyyMMdd encoding.");
      }
      return TimeZoneInfo.ConvertTimeToUtc(
        DateTime.SpecifyKind(date, DateTimeKind.Unspecified),
        FindTimeZone(timeZoneId));
    }

    private static TimeZoneInfo FindTimeZone(string timeZoneId) {
      string requested = string.IsNullOrWhiteSpace(timeZoneId)
        ? TimeZoneInfo.Utc.Id : timeZoneId;
      try {
        return TimeZoneInfo.FindSystemTimeZoneById(requested);
      } catch (TimeZoneNotFoundException) {
        string fallback = requested == PacificTimeZoneId
          ? PacificIanaTimeZoneId
          : requested == PacificIanaTimeZoneId
            ? PacificTimeZoneId
            : null;
        if (fallback == null) {
          throw;
        }
        return TimeZoneInfo.FindSystemTimeZoneById(fallback);
      }
    }
  }

  // One layer in the dome's compositing stack: which visualizer produces it,
  // how it blends, its opacity, and whether it's muted. An XML-serializable POCO
  // persisted inside config.domeLayerStack.
  //
  // Instances are treated as immutable once published to the operator thread:
  // UI/web writers always replace the whole domeLayerStack list (snapshot swap)
  // rather than mutating an existing settings object in place.
  public class DomeLayerSettings {
    // Stable identity of this configured occurrence. Older XML omits it; the
    // LayerStackService assigns one during normalization and every writer then
    // persists it. Renderer IDs identify kinds, instance IDs identify layers.
    public string InstanceId { get; set; }
    // Stable string id of the layerable visualizer, e.g. "radial".
    public string VisualizerKey { get; set; }
    // The DomeBlend.Id of how this layer combines with the composite below
    // it. A string (not the blend object) because it's the persisted form —
    // XSerializer writes it verbatim, exactly the names the retired
    // DomeBlendMode enum used to serialize, so old config/scene files load
    // unchanged. Resolve with DomeBlend.FromId; consumers cache the result.
    public string BlendMode { get; set; } = DomeBlend.Default.Id;
    // 0..1, applied before the blend.
    public double Opacity { get; set; } = 1.0;
    // Mute without removing from the stack.
    public bool Enabled { get; set; } = true;

    // Free-text note the user leaves for themselves (e.g. "use this to make
    // things monochrome"). Null by default; never populated by defaults or
    // schema logic, purely user-authored. Carried through scenes like every
    // other field on this POCO (DomeScene.Layers reuses DomeLayerSettings).
    public string Notes { get; set; }

    // Per-renderer and per-operation parameter overrides. Separate bags make
    // ownership explicit and allow a renderer and operation to use the same key
    // without colliding. Missing keys use the descriptor defaults.
    //
    // Null by default on purpose: XSerializer deserializes dictionary members by
    // Add-ing into the existing instance, so a non-null initializer would
    // double-up the persisted entries on load — the same null-by-default rule
    // domeLayerStack and domeCableMapping already follow.
    public Dictionary<string, double> RendererParams { get; set; }
    public Dictionary<string, double> OperationParams { get; set; }

    public static DomeLayerSettings ForInstance(
      IList<DomeLayerSettings> stack, string instanceId
    ) {
      if (stack == null || instanceId == null) {
        return null;
      }
      for (int i = 0; i < stack.Count; i++) {
        DomeLayerSettings layer = stack[i];
        if (layer != null && layer.InstanceId == instanceId) {
          return layer;
        }
      }
      return null;
    }

  }

  // ---- Per-layer parameter schemas ---------------------------------------
  // Definitions are kept outside the serializer-facing layer DTO and are
  // attached explicitly to LayerCatalog registrations.
  internal static class LayerParameterSchemas {
    // Single source of truth for every tunable registered in LayerCatalog.
    // Both UIs render editors generically from these, the
    // snapshot compiler applies their defaults, and the compositor reads the
    // CompositorConsumed ones. Adding a visualizer parameter needs no UI code.

    internal static readonly DomeLayerParam[] NoParams =
      Array.Empty<DomeLayerParam>();

    // Descriptors shared by more than one visualizer.
    private static readonly DomeLayerParam RotationSpeedParam =
      new DomeLayerParam {
        Key = "rotationSpeed", Label = "Rotation Speed",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 0.125, Default = 1.0,
      };
    private static readonly DomeLayerParam GradientSpeedParam =
      new DomeLayerParam {
        Key = "gradientSpeed", Label = "Gradient Speed",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 0.125, Default = 1.0,
      };
    private static readonly DomeLayerParam TwinkleDensityParam =
      new DomeLayerParam {
        Key = "twinkleDensity", Label = "Twinkle Density",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 0.001, Step = 0.0001, Default = 0,
      };

    // Which of the eight palette banks (colorPalette slots bank*8 .. bank*8+7)
    // this layer draws its colors from. Shared by every palette-consuming layer
    // (the ones that call dome.Get*Color); the visualizer reads it once per frame
    // and passes it into the color lookups. Default 0 = bank 0 = the historical
    // single live palette, so a layer with no "palette" key renders unchanged.
    private static readonly DomeLayerParam PaletteBankParam =
      new DomeLayerParam {
        Key = "palette", Label = "Palette",
        Type = DomeLayerParamType.Enum,
        Options = new string[] {
          "Palette 1", "Palette 2", "Palette 3", "Palette 4",
          "Palette 5", "Palette 6", "Palette 7", "Palette 8",
        },
        Default = 0,
      };

    // Radial's tuning, formerly the domeRadial* cluster of global properties.
    internal static readonly DomeLayerParam[] RadialParams = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "effect", Label = "Effect",
        Type = DomeLayerParamType.Enum,
        Options = new string[] { "Radar", "Pulse", "Spiral", "Bubbles" },
        Default = 0,
      },
      new DomeLayerParam {
        Key = "size", Label = "Size",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 0.05, Default = 0.1,
      },
      new DomeLayerParam {
        Key = "frequency", Label = "Frequency",
        Type = DomeLayerParamType.Double,
        Min = 1, Max = 12, Step = 1, Default = 1,
      },
      new DomeLayerParam {
        Key = "centerAngle", Label = "Center Angle",
        Type = DomeLayerParamType.Double,
        Min = -Math.PI, Max = Math.PI, Step = 0.01, Default = 0,
      },
      new DomeLayerParam {
        Key = "centerDistance", Label = "Center Distance",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 1, Step = 0.01, Default = 0,
      },
      new DomeLayerParam {
        Key = "centerSpeed", Label = "Center Speed",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 0.125, Default = 0,
      },
      RotationSpeedParam,
      GradientSpeedParam,
      PaletteBankParam,
    };

    // Volume's tuning (animation size was a MainWindow combo, the speeds were
    // shared globals).
    internal static readonly DomeLayerParam[] VolumeParams = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "animationSize", Label = "Animation Size",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 1, Default = 4,
      },
      RotationSpeedParam,
      GradientSpeedParam,
      PaletteBankParam,
    };

    // Race repurposed two knobs that nominally belonged to other visualizers;
    // per-layer they get honest names.
    internal static readonly DomeLayerParam[] RaceParams = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "speed", Label = "Speed",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 0.125, Default = 1.0,
      },
      new DomeLayerParam {
        Key = "spacing", Label = "Spacing",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 1, Step = 0.01, Default = 0.1,
      },
      PaletteBankParam,
    };

    // Splat and Snakes have no other tunables today; the palette bank picker is
    // their only per-layer param.
    internal static readonly DomeLayerParam[] SplatParams = new DomeLayerParam[] {
      PaletteBankParam,
    };
    internal static readonly DomeLayerParam[] SnakesParams = new DomeLayerParam[] {
      PaletteBankParam,
    };

    internal static readonly DomeLayerParam[] TwinkleParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "density", Label = "Density",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 0.001, Step = 0.0001, Default = 0,
        },
      };

    // Paintbrush's tuning: the metaball size shared domeRadialSize's knob; the
    // ripple steps and twinkle density were its own globals.
    internal static readonly DomeLayerParam[] PaintbrushParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "size", Label = "Size",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 4, Step = 0.05, Default = 0.1,
        },
        TwinkleDensityParam,
        new DomeLayerParam {
          Key = "rippleCDStep", Label = "Ripple Cooldown",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 10, Step = 0.1, Default = 1,
        },
        new DomeLayerParam {
          Key = "rippleStep", Label = "Ripple Speed",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 4, Step = 0.1, Default = 1,
        },
      };

    // Trigger params shared by the triggerable one-shot layers (Ripple/Stamp),
    // read by LayerTrigger (docs/triggers.md). `button` binds an optional wand
    // button; `level`/`interval` tune the Audio source and only bite when
    // trigger = Audio. `trigger` itself is declared per-layer below because its
    // default differs (Ripple = Beat, Stamp = Audio). Manual (the native/web
    // Fire button) is always live regardless of these.
    private static readonly DomeLayerParam TriggerButtonParam =
      new DomeLayerParam {
        Key = "button", Label = "Button",
        Type = DomeLayerParamType.Enum,
        Options = new string[] { "Unbound", "1", "2", "3" },
        Default = 0,
      };
    private static readonly DomeLayerParam TriggerLevelParam =
      new DomeLayerParam {
        Key = "level", Label = "Loudness Threshold",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 1, Step = 0.01, Default = 0.3,
      };
    private static readonly DomeLayerParam TriggerIntervalParam =
      new DomeLayerParam {
        Key = "interval", Label = "Audio Interval (ms)",
        Type = DomeLayerParamType.Double,
        Min = 50, Max = 4000, Step = 50, Default = 800,
      };
    private static readonly DomeLayerParam StampTriggerIntervalParam =
      new DomeLayerParam {
        Key = "interval", Label = "Audio Interval (ms)",
        Type = DomeLayerParamType.Double,
        Min = 2000, Max = 8000, Step = 50, Default = 2000,
      };
    // Options for the autonomous trigger-source selector: index matches the
    // `source` arg LayerTrigger.Fired takes. Manual = fire only via the Fire
    // button / bound wand button (no autonomous source).
    private static readonly string[] TriggerSourceOptions =
      new string[] { "Manual", "Beat", "Audio" };

    // Ripple's own tuning, independent of the copy still fused inside
    // Paintbrush (docs/layers_inventory.md: twinkle's precedent — the two
    // copies are separate and unremoved until the rest of the disassembly
    // lands). Firing is driven by LayerTrigger (docs/triggers.md); rippleStep
    // is the playhead
    // expansion speed, unrelated to the trigger.
    internal static readonly DomeLayerParam[] RippleParams = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "rippleStep", Label = "Ripple Speed",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 0.1, Default = 1,
      },
      new DomeLayerParam {
        Key = "desaturation", Label = "Desaturation",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 1, Step = 0.05, Default = 0,
      },
      new DomeLayerParam {
        Key = "trigger", Label = "Trigger",
        Type = DomeLayerParamType.Enum,
        Options = TriggerSourceOptions, Default = 1, // Beat
      },
      TriggerButtonParam,
      TriggerLevelParam,
      TriggerIntervalParam,
    };

    // Stamp's own tuning. Firing is driven by LayerTrigger (docs/triggers.md),
    // defaulting to the Audio source;
    // level/interval tune that source.
    internal static readonly DomeLayerParam[] StampParams = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "trigger", Label = "Trigger",
        Type = DomeLayerParamType.Enum,
        Options = TriggerSourceOptions, Default = 2, // Audio
      },
      TriggerButtonParam,
      TriggerLevelParam,
      StampTriggerIntervalParam,
    };

    // An autonomous stack of concentric rings centered on the dome's vertical
    // axis. Rings travel from the crown toward the rim continuously; the
    // renderer gives each one a stable variation in speed, thickness, and
    // brightness, with `variation` controlling how far those traits spread
    // from their base values.
    internal static readonly DomeLayerParam[] TunnelParams = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "count", Label = "Ring Count",
        Type = DomeLayerParamType.Double,
        Min = 3, Max = 24, Step = 1, Default = 12,
      },
      new DomeLayerParam {
        Key = "speed", Label = "Travel Speed",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 1.5, Step = 0.025, Default = 0.18,
      },
      new DomeLayerParam {
        Key = "thickness", Label = "Ring Thickness",
        Type = DomeLayerParamType.Double,
        Min = 0.005, Max = 0.12, Step = 0.005, Default = 0.025,
      },
      new DomeLayerParam {
        Key = "brightness", Label = "Brightness",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 1, Step = 0.05, Default = 1,
      },
      new DomeLayerParam {
        Key = "variation", Label = "Ring Variation",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 1, Step = 0.05, Default = 0.8,
      },
      new DomeLayerParam {
        Key = "bindOrientation", Label = "Bind to Orientation",
        Type = DomeLayerParamType.Bool, Default = 0,
      },
      new DomeLayerParam {
        Key = "color", Label = "Color",
        Type = DomeLayerParamType.Color,
        Min = 0, Max = 0xFFFFFF, Default = 0xFFFFFF,
      },
    };

    // Metaball's own tuning. Contours default off.
    internal static readonly DomeLayerParam[] MetaballParams = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "size", Label = "Size",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 0.05, Default = 0.1,
      },
      new DomeLayerParam {
        Key = "contours", Label = "Show Contours",
        Type = DomeLayerParamType.Bool,
        Default = 0,
      },
      // Triggerable (docs/triggers.md): a wand button press flashes the
      // blob briefly bigger via LayerTrigger, replacing the old hard-coded
      // per-device bonus that used to live in OrientationCenter. Unbound
      // (default) means the burst is Manual-fire-only.
      new DomeLayerParam {
        Key = "button", Label = "Button",
        Type = DomeLayerParamType.Enum,
        Options = new string[] { "Unbound", "1", "2", "3" },
        Default = 0,
      },
    };

    // A signed counterpart to Metaball: each orientation contributes a +1
    // point charge at Spot and a -1 charge at NegSpot. Strength controls the
    // exponential display compression, while the two colors make the sign of
    // the superposed potential explicit. The field's zero/cancellation line is
    // transparent, so it composes naturally over lower layers. The line
    // controls trace equally spaced streamlines from each positive pole to its
    // negative antipode; zero lines leaves only the signed potential shading.
    internal static readonly DomeLayerParam[] MagneticFieldParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "strength", Label = "Field Strength",
          Type = DomeLayerParamType.Double,
          Min = 0.1, Max = 4, Step = 0.05, Default = 1,
        },
        new DomeLayerParam {
          Key = "positiveColor", Label = "+1 Color",
          Type = DomeLayerParamType.Color,
          Min = 0, Max = 0xFFFFFF, Default = 0xFF3B30,
        },
        new DomeLayerParam {
          Key = "negativeColor", Label = "-1 Color",
          Type = DomeLayerParamType.Color,
          Min = 0, Max = 0xFFFFFF, Default = 0x3478F6,
        },
        new DomeLayerParam {
          Key = "lineCount", Label = "Field Lines",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 24, Step = 2, Default = 12,
        },
        new DomeLayerParam {
          Key = "lineWidth", Label = "Line Width",
          Type = DomeLayerParamType.Double,
          Min = 0.01, Max = 0.15, Step = 0.005, Default = 0.035,
        },
      };

    // Background's only tunable: the flat color it paints every pixel.
    internal static readonly DomeLayerParam[] BackgroundParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "color", Label = "Color",
          Type = DomeLayerParamType.Color,
          Min = 0, Max = 0xFFFFFF, Default = 0xFFFFFF,
        },
      };

    // Earth is a literal equirectangular texture wrapped around the dome's
    // baked unit hemisphere. Its pole axis follows the shared orientation
    // spotlight/idle center; the only independent motion is longitude advancing
    // around that axis. Speed is measured in revolutions per second and may be
    // negative to reverse the spin.
    internal static readonly DomeLayerParam[] EarthParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "spinSpeed", Label = "Spin Speed (rev/s)",
          Type = DomeLayerParamType.Double,
          Min = -0.25, Max = 0.25, Step = 0.005, Default = 0.02,
        },
      };

    // Astronomy paints a clock-driven Black Rock City sky onto the dome. North
    // Heading rotates true north around the physical dome (0 = projected +Y,
    // increasing clockwise); Time scrubs one week forward from midnight on the
    // selected Black Rock City date.
    internal static readonly DomeLayerParam[] AstronomyParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "northHeading", Label = "North Heading (deg clockwise)",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 359, Step = 1, Default = 0,
        },
        new DomeLayerParam {
          Key = "startDate", Label = "Start Date",
          Type = DomeLayerParamType.Date,
          Default = 0,
          TimeZoneId = DomeLayerDate.PacificTimeZoneId,
        },
        new DomeLayerParam {
          Key = "timeOffsetHours", Label = "Time (hours from start)",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 168, Step = 1, Default = 0,
        },
        new DomeLayerParam {
          Key = "showDaytimeSky", Label = "Show Daytime Sky",
          Type = DomeLayerParamType.Bool,
          Default = 1,
        },
        new DomeLayerParam {
          Key = "showNighttimeSky", Label = "Show Nighttime Sky",
          Type = DomeLayerParamType.Bool,
          Default = 1,
        },
        new DomeLayerParam {
          Key = "playbackSpeed", Label = "Playback Speed (x)",
          Type = DomeLayerParamType.Double,
          Min = 0.5, Max = 8, Step = 0.1, Default = 1,
        },
        new DomeLayerParam {
          Key = "loop", Label = "Loop",
          Type = DomeLayerParamType.Bool,
          Default = 0,
        },
      };

    // Flash is Background's flat color fill made momentary: instead of painting
    // every frame it paints the whole dome only when LayerTrigger fires, then
    // fades out (docs/triggers.md — same fill-then-Fade playhead as Stamp, using
    // domeGlobalFadeSpeed). So it shares Background's `color` and the full
    // trigger param set (Ripple/Stamp's), defaulting to the Beat source for a
    // strobe-on-the-beat. `level`/`interval` tune the Audio source; Manual (the
    // native Fire button) and a bound wand `button` fire it regardless.
    internal static readonly DomeLayerParam[] FlashParams = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "color", Label = "Color",
        Type = DomeLayerParamType.Color,
        Min = 0, Max = 0xFFFFFF, Default = 0xFFFFFF,
      },
      new DomeLayerParam {
        Key = "trigger", Label = "Trigger",
        Type = DomeLayerParamType.Enum,
        Options = TriggerSourceOptions, Default = 1, // Beat
      },
      TriggerButtonParam,
      TriggerLevelParam,
      TriggerIntervalParam,
    };

    // Visualizer-consumed params for the wave layer: read in Visualize().
    internal static readonly DomeLayerParam[] WaveParams = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "bandWidth", Label = "Band Width",
        Type = DomeLayerParamType.Double,
        Min = 0.02, Max = 1.5, Step = 0.01, Default = 0.12,
      },
      new DomeLayerParam {
        Key = "speed", Label = "Sweep Speed",
        Type = DomeLayerParamType.Double,
        Min = -2, Max = 2, Step = 0.05, Default = 0.3,
      },
      new DomeLayerParam {
        Key = "centerAngle", Label = "Center Angle",
        Type = DomeLayerParamType.Double,
        Min = -Math.PI, Max = Math.PI, Step = 0.01, Default = 0,
      },
      new DomeLayerParam {
        Key = "centerDistance", Label = "Center Distance",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 1, Step = 0.01, Default = 0,
      },
      new DomeLayerParam {
        Key = "color", Label = "Color",
        Type = DomeLayerParamType.Color,
        Min = 0, Max = 0xFFFFFF, Default = 0xFFFFFF,
      },
      // Triggerable layers (docs/triggers.md): Loop is today's forever-cycle
      // behavior (default, so existing saved layers are unaffected); OneShot
      // plays the band 0->1 once per trigger fire, then clears until re-fired.
      new DomeLayerParam {
        Key = "mode", Label = "Playback Mode",
        Type = DomeLayerParamType.Enum,
        Options = new string[] { "Loop", "OneShot" },
        Default = 0,
      },
      // Only meaningful when mode = OneShot. A OneShot layer is always fireable
      // by the Manual (native/web Fire button) source; this selects whether a
      // wand button *also* fires it. Index == the wand actionFlag value, so
      // Unbound (0) means "no wand trigger, Manual only" and 1/2/3 are the
      // firmware button codes (LayerTrigger).
      new DomeLayerParam {
        Key = "button", Label = "Button",
        Type = DomeLayerParamType.Enum,
        Options = new string[] { "Unbound", "1", "2", "3" },
        Default = 0,
      },
    };

    // Point Cloud's tuning, all visualizer-consumed (read in Visualize()).
    // `count` reseeds the spot lattice when it
    // changes; the rest tune the per-frame physics and the drawn spot size.
    internal static readonly DomeLayerParam[] PointCloudParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "count", Label = "Spot Count",
          Type = DomeLayerParamType.Double,
          Min = 4, Max = 320, Step = 1, Default = 48,
        },
        new DomeLayerParam {
          Key = "spotSize", Label = "Spot Size",
          Type = DomeLayerParamType.Double,
          Min = 0.02, Max = 0.5, Step = 0.01, Default = 0.14,
        },
        new DomeLayerParam {
          Key = "pushRadius", Label = "Push Radius",
          Type = DomeLayerParamType.Double,
          Min = 0.05, Max = 1.5, Step = 0.05, Default = 0.5,
        },
        new DomeLayerParam {
          Key = "pushStrength", Label = "Push Strength",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 0.1, Step = 0.005, Default = 0.02,
        },
        new DomeLayerParam {
          Key = "springStrength", Label = "Spring Strength",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 0.1, Step = 0.005, Default = 0.01,
        },
        new DomeLayerParam {
          Key = "damping", Label = "Damping",
          Type = DomeLayerParamType.Double,
          Min = 0.5, Max = 0.99, Step = 0.01, Default = 0.9,
        },
      };

    // Shooting Star's tuning, all visualizer-consumed (read in Visualize()).
    // Dots are born just off the rim and
    // accelerate toward the wand aim point; `spawnRate` is stars/sec, the
    // physics knobs are in centered (u,v) units per second, `trail` is the
    // per-second brightness retention of the streak, and `homing` re-reads the
    // live wand each frame (curving toward a moving wand) vs. staying ballistic
    // toward the aim captured at spawn.
    internal static readonly DomeLayerParam[] ShootingStarParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "spawnRate", Label = "Spawn Rate",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 10, Step = 0.25, Default = 2,
        },
        new DomeLayerParam {
          Key = "accel", Label = "Acceleration",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 32, Step = 0.1, Default = 2.0,
        },
        new DomeLayerParam {
          Key = "maxSpeed", Label = "Max Speed",
          Type = DomeLayerParamType.Double,
          Min = 0.1, Max = 16, Step = 0.1, Default = 1.5,
        },
        new DomeLayerParam {
          Key = "size", Label = "Dot Size",
          Type = DomeLayerParamType.Double,
          Min = 0.01, Max = 0.3, Step = 0.01, Default = 0.05,
        },
        new DomeLayerParam {
          Key = "homing", Label = "Homing",
          Type = DomeLayerParamType.Bool,
          Default = 1,
        },
        // Trigger cluster (docs/triggers.md): each fire launches one extra star
        // on top of the steady spawnRate. Defaults to Beat so stars streak in on
        // the beat; level/interval tune the Audio source, button binds a wand
        // button, and Manual (the Fire button) is always live.
        new DomeLayerParam {
          Key = "trigger", Label = "Trigger",
          Type = DomeLayerParamType.Enum,
          Options = TriggerSourceOptions, Default = 1, // Beat
        },
        TriggerButtonParam,
        TriggerLevelParam,
        TriggerIntervalParam,
        PaletteBankParam,
      };

    // Sparkler is Shooting Star in reverse: particles are emitted continuously
    // from the current wand/idle aim point and sent in random directions at
    // constant speed. Every trigger births one extra particle on top of the
    // emission rate. The buffer's normal global fade supplies their trails.
    internal static readonly DomeLayerParam[] SparklerParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "emissionRate", Label = "Emission Rate",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 30, Step = 1, Default = 8,
        },
        new DomeLayerParam {
          Key = "speed", Label = "Speed",
          Type = DomeLayerParamType.Double,
          Min = 0.1, Max = 16, Step = 0.1, Default = 1.5,
        },
        new DomeLayerParam {
          Key = "size", Label = "Dot Size",
          Type = DomeLayerParamType.Double,
          Min = 0.01, Max = 0.3, Step = 0.01, Default = 0.05,
        },
        new DomeLayerParam {
          Key = "trigger", Label = "Trigger",
          Type = DomeLayerParamType.Enum,
          Options = TriggerSourceOptions, Default = 1, // Beat
        },
        TriggerButtonParam,
        TriggerLevelParam,
        TriggerIntervalParam,
        PaletteBankParam,
      };

    // Gyroscope's tuning, all visualizer-consumed (read in Visualize()). The
    // gimbal motion is driven by device/idle orientation
    // (OrientationCenter), not a clock, so there are no spin/precession/tilt
    // knobs: `ringWidth` is the great-circle band thickness (dot-product units);
    // `rotorRate` is the orbit speed of the bright highlight chasing the rotor
    // rim (the flywheel's own DOF). The idle-drift wander speed (used when no
    // wand is moving) is not exposed as a knob — the visualizer feeds a fixed
    // level into the shared OrientationCenter. The three nested gimbal rings
    // (outer/middle/inner) draw their colors from the live palette like every
    // other palette-consuming layer: `palette` picks the bank, and the visualizer
    // reads its first three slots (relative 0/1/2) for the outer/middle/inner
    // rings, scaling each by the ring's cross-section falloff so they still fade
    // to black at the edges.
    internal static readonly DomeLayerParam[] GyroscopeParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "ringWidth", Label = "Ring Width",
          Type = DomeLayerParamType.Double,
          Min = 0.01, Max = 0.05, Step = 0.005, Default = 0.03,
        },
        new DomeLayerParam {
          Key = "rotorRate", Label = "Rotor Speed",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 6, Step = 0.1, Default = 2.2,
        },
        PaletteBankParam,
      };

    // Noise Cloud's tuning, all visualizer-consumed (read in Visualize()). It
    // emits an animated fractal-value-noise field tinting
    // `color` — meant to sit under Multiply/Add to break up a flat layer below.
    // `scale` is the spatial frequency (bigger = smaller blobs), `speed` morphs
    // the field in place through a time axis (0 = frozen, no directional drift),
    // `octaves` adds finer detail (fractal summation), and `contrast` steepens
    // the field around its midtone so the texture reads stronger without
    // clipping to solid.
    internal static readonly DomeLayerParam[] NoiseCloudParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "scale", Label = "Scale",
          Type = DomeLayerParamType.Double,
          Min = 0.5, Max = 8, Step = 0.1, Default = 2.5,
        },
        new DomeLayerParam {
          Key = "speed", Label = "Morph Speed",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 2, Step = 0.02, Default = 0.2,
        },
        new DomeLayerParam {
          Key = "octaves", Label = "Detail",
          Type = DomeLayerParamType.Double,
          Min = 1, Max = 4, Step = 1, Default = 2,
        },
        new DomeLayerParam {
          Key = "contrast", Label = "Contrast",
          Type = DomeLayerParamType.Double,
          Min = 1, Max = 6, Step = 0.25, Default = 2.5,
        },
        new DomeLayerParam {
          Key = "color", Label = "Color",
          Type = DomeLayerParamType.Color,
          Min = 0, Max = 0xFFFFFF, Default = 0xFFFFFF,
        },
      };

    // A stateless polar flow field that reads as a dense particle system
    // without maintaining particles. Whirlpool keeps the advected noise
    // continuous and shapes it into spiral arms; Sandstorm thresholds a finer
    // field into grains. Both retain faded field history as trails. All work is
    // O(dome pixels), independent of apparent density.
    internal static readonly DomeLayerParam[] VortexParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "style", Label = "Style",
          Type = DomeLayerParamType.Enum,
          Options = new string[] { "Whirlpool", "Sandstorm" }, Default = 0,
        },
        new DomeLayerParam {
          Key = "speed", Label = "Spin Speed",
          Type = DomeLayerParamType.Double,
          Min = -1, Max = 1, Step = 0.125, Default = 1,
        },
        new DomeLayerParam {
          Key = "audioBrightness", Label = "Audio Brightness",
          Type = DomeLayerParamType.Bool, Default = 0,
        },
        new DomeLayerParam {
          Key = "audioSpeed", Label = "Beat Speed",
          Type = DomeLayerParamType.Bool, Default = 0,
        },
        new DomeLayerParam {
          Key = "twist", Label = "Twist",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 8, Step = 0.25, Default = 3,
        },
        new DomeLayerParam {
          Key = "scale", Label = "Grain Scale",
          Type = DomeLayerParamType.Double,
          Min = 2, Max = 32, Step = 0.5, Default = 10,
        },
        new DomeLayerParam {
          Key = "density", Label = "Density",
          Type = DomeLayerParamType.Double,
          Min = 0.05, Max = 0.95, Step = 0.05, Default = 0.55,
        },
        new DomeLayerParam {
          Key = "coreSize", Label = "Core Size",
          Type = DomeLayerParamType.Double,
          Min = 0.01, Max = 0.5, Step = 0.01, Default = 0.12,
        },
        new DomeLayerParam {
          Key = "inflow", Label = "Inflow",
          Type = DomeLayerParamType.Double,
          Min = -2, Max = 2, Step = 0.05, Default = 0.25,
        },
        new DomeLayerParam {
          Key = "turbulence", Label = "Turbulence",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 2, Step = 0.05, Default = 0.65,
        },
        new DomeLayerParam {
          Key = "color", Label = "Color",
          Type = DomeLayerParamType.Color,
          Min = 0, Max = 0xFFFFFF, Default = 0xD8B878,
        },
      };

    // Caustics' analytic tuning, all visualizer-consumed (read in Visualize()).
    // `method` is the fidelity ladder; `scale` is the feature-size multiplier,
    // `speed` the churn rate, `sharpness` the filament-thinness exponent,
    // `brightness` the output gain, and `color` the tint. Ripple Tank is a
    // separate orientation-only layer below.
    internal static readonly DomeLayerParam[] CausticsParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "method", Label = "Method",
          Type = DomeLayerParamType.Enum,
          Options = new string[] { "Shimmer", "Interference", "Lens" },
          Default = 1, // Interference — the authentic caustic look
        },
        new DomeLayerParam {
          Key = "scale", Label = "Scale",
          Type = DomeLayerParamType.Double,
          Min = 1, Max = 40, Step = 0.5, Default = 14,
        },
        new DomeLayerParam {
          Key = "speed", Label = "Speed",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 4, Step = 0.125, Default = 1,
        },
        new DomeLayerParam {
          Key = "sharpness", Label = "Sharpness",
          Type = DomeLayerParamType.Double,
          Min = 1, Max = 12, Step = 0.5, Default = 5,
        },
        new DomeLayerParam {
          Key = "brightness", Label = "Brightness",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 2, Step = 0.05, Default = 1,
        },
        new DomeLayerParam {
          Key = "color", Label = "Color",
          Type = DomeLayerParamType.Color,
          Min = 0, Max = 0xFFFFFF, Default = 0xCCF7FF, // pale cyan-white
        },
      };

    // Ripple Tank is driven exclusively by live orientation devices. Its
    // contact patch is fixed at a small renderer-defined size; wake strength
    // deliberately has no parameter because the renderer derives it from
    // sensor angular velocity.
    internal static readonly DomeLayerParam[] RippleTankParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "speed", Label = "Wave Speed",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 3, Step = 0.125, Default = 1,
        },
        new DomeLayerParam {
          Key = "damping", Label = "Damping",
          Type = DomeLayerParamType.Double,
          Min = 0.02, Max = 0.05, Step = 0.01, Default = 0.02,
        },
        new DomeLayerParam {
          Key = "sharpness", Label = "Sharpness",
          Type = DomeLayerParamType.Double,
          Min = 1, Max = 12, Step = 0.5, Default = 5,
        },
        new DomeLayerParam {
          Key = "brightness", Label = "Brightness",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 2, Step = 0.05, Default = 1,
        },
        new DomeLayerParam {
          Key = "color", Label = "Color",
          Type = DomeLayerParamType.Color,
          Min = 0, Max = 0xFFFFFF, Default = 0xCCF7FF,
        },
      };

  }

}

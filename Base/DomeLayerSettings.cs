using System;
using System.Collections.Generic;

namespace Spectrum.Base {

  // The value type of a per-layer parameter. Values live in the bag as double
  // regardless: Bool is 0/1, Enum is the index into DomeLayerParam.Options,
  // Color is a packed 0xRRGGBB int reinterpreted as a double.
  public enum DomeLayerParamType { Double, Bool, Enum, Color }

  // Static schema for one tunable on a layer (or on a blend mode). The bag on a
  // DomeLayerSettings stores only values keyed by DomeLayerParam.Key; everything
  // else here (range, label, default, which consumer reads it) is compile-time
  // metadata read identically by both UIs, the resolver, and GetParam fallbacks.
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
    // true => read by the compositor (CompositeBlend) once per frame, never by
    // the visualizer. false => read by the visualizer in Visualize().
    public bool CompositorConsumed { get; set; }
    // Name of the retired top-level Configuration property this param replaced
    // (e.g. "domeRadialSize"), or null for params that never were global. Read
    // only by LegacyLayerParamMigration, which seeds the value from a config
    // file that predates the per-layer move. Defaults deliberately equal the
    // old property defaults so an absent bag reproduces pre-params behavior.
    public string LegacySetting { get; set; }
  }

  // One layer in the dome's compositing stack: which visualizer produces it,
  // how it blends, its opacity, and whether it's muted. An XML-serializable POCO
  // persisted inside config.domeLayerStack.
  //
  // Instances are treated as immutable once published to the operator thread:
  // UI/web writers always replace the whole domeLayerStack list (snapshot swap)
  // rather than mutating an existing settings object in place.
  public partial class DomeLayerSettings {
    // Stable identity of this configured occurrence. Older XML omits it; the
    // LayerStackService assigns one during normalization and every writer then
    // persists it. Renderer IDs identify kinds, instance IDs identify layers.
    public string InstanceId { get; set; }
    // Stable string id of the layerable visualizer, e.g. "radial". See
    // DomeLayerVisualizer.LayerKey and LegacyVisKeys below.
    public string VisualizerKey { get; set; }
    // The DomeBlend.Name of how this layer combines with the composite below
    // it. A string (not the blend object) because it's the persisted form —
    // XSerializer writes it verbatim, exactly the names the retired
    // DomeBlendMode enum used to serialize, so old config/scene files load
    // unchanged. Resolve with DomeBlend.FromName; consumers cache the result.
    public string BlendMode { get; set; } = DomeBlend.Default.Name;
    // 0..1, applied before the blend.
    public double Opacity { get; set; } = 1.0;
    // Mute without removing from the stack.
    public bool Enabled { get; set; } = true;

    // Free-text note the user leaves for themselves (e.g. "use this to make
    // things monochrome"). Null by default; never populated by defaults or
    // schema logic, purely user-authored. Carried through scenes like every
    // other field on this POCO (DomeScene.Layers reuses DomeLayerSettings).
    public string Notes { get; set; }

    // Per-layer parameter overrides: key (a DomeLayerParam.Key from the layer's
    // schema, or its blend's) -> value. A missing key means "use the descriptor
    // default" everywhere (see GetParam), so an absent/empty bag reproduces the
    // pre-params behavior exactly.
    //
    // Null by default on purpose: XSerializer deserializes dictionary members by
    // Add-ing into the existing instance, so a non-null initializer would
    // double-up the persisted entries on load — the same null-by-default rule
    // domeLayerStack and domeCableMapping already follow.
    public Dictionary<string, double> Params { get; set; }

    // Value for `key` from this layer's bag, or `fallback` if the bag is
    // null/missing the key. Callers pass the descriptor Default as `fallback` so
    // defaults stay single-sourced in LayerCatalog / DomeBlend.Params.
    public double GetParam(string key, double fallback) {
      return this.Params != null && this.Params.TryGetValue(key, out double v)
        ? v : fallback;
    }

    // The stack entry naming `key` (or null if none), so a visualizer can read
    // its own params in Visualize(). Allocation-free linear scan of the small
    // immutable snapshot. This compatibility lookup returns the first renderer
    // occurrence; instance-aware runtime paths use ForInstance instead.
    public static DomeLayerSettings ForKey(
      IList<DomeLayerSettings> stack, string key
    ) {
      if (stack == null) {
        return null;
      }
      for (int i = 0; i < stack.Count; i++) {
        DomeLayerSettings layer = stack[i];
        if (layer != null && layer.VisualizerKey == key) {
          return layer;
        }
      }
      return null;
    }

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

    // The legacy domeActiveVis int -> layer key mapping, kept so config
    // migration can synthesize a stack from an old file's selector (the
    // domeActiveVis property itself is retired). Index == the old magic int.
    public static readonly string[] LegacyVisKeys = new string[] {
      "volume", "radial", "race", "snakes", "quaternion-test",
      "quaternion-paintbrush", "splat", "tv-static",
    };
    public static string KeyForLegacyVis(int vis) {
      return vis >= 0 && vis < LegacyVisKeys.Length ? LegacyVisKeys[vis] : null;
    }

  }

  // ---- Per-layer parameter schemas ---------------------------------------
  // Definitions are kept outside the serializer-facing layer DTO and are
  // attached explicitly to LayerCatalog registrations.
  internal static class LayerParameterSchemas {
    // Single source of truth for every tunable registered in LayerCatalog.
    // Both UIs render editors generically from these, the
    // compositor resolver reads the CompositorConsumed ones, and every GetParam
    // fallback passes the matching Default. Adding a param to a visualizer is one
    // entry here with zero UI code.

    internal static readonly DomeLayerParam[] NoParams =
      Array.Empty<DomeLayerParam>();

    // Descriptors shared by more than one visualizer. Historically one global
    // knob drove all of these consumers at once (the LegacySetting property);
    // per-layer they are independent values that happen to share a schema.
    private static readonly DomeLayerParam RotationSpeedParam =
      new DomeLayerParam {
        Key = "rotationSpeed", Label = "Rotation Speed",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 0.125, Default = 1.0,
        LegacySetting = "domeVolumeRotationSpeed",
      };
    private static readonly DomeLayerParam GradientSpeedParam =
      new DomeLayerParam {
        Key = "gradientSpeed", Label = "Gradient Speed",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 0.125, Default = 1.0,
        LegacySetting = "domeGradientSpeed",
      };
    private static readonly DomeLayerParam TwinkleDensityParam =
      new DomeLayerParam {
        Key = "twinkleDensity", Label = "Twinkle Density",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 0.001, Step = 0.0001, Default = 0,
        LegacySetting = "domeTwinkleDensity",
      };

    // Which of the eight palette banks (colorPalette slots bank*8 .. bank*8+7)
    // this layer draws its colors from. Shared by every palette-consuming layer
    // (the ones that call dome.Get*Color); the visualizer reads it once per frame
    // and passes it into the color lookups. Default 0 = bank 0 = the historical
    // single live palette, so a layer with no "palette" key renders unchanged.
    // No LegacySetting: the old global colorPaletteIndex was retired, not a
    // per-layer value to migrate from.
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
        LegacySetting = "domeRadialEffect",
      },
      new DomeLayerParam {
        Key = "size", Label = "Size",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 0.05, Default = 0.1,
        LegacySetting = "domeRadialSize",
      },
      new DomeLayerParam {
        Key = "frequency", Label = "Frequency",
        Type = DomeLayerParamType.Double,
        Min = 1, Max = 12, Step = 1, Default = 1,
        LegacySetting = "domeRadialFrequency",
      },
      new DomeLayerParam {
        Key = "centerAngle", Label = "Center Angle",
        Type = DomeLayerParamType.Double,
        Min = -Math.PI, Max = Math.PI, Step = 0.01, Default = 0,
        LegacySetting = "domeRadialCenterAngle",
      },
      new DomeLayerParam {
        Key = "centerDistance", Label = "Center Distance",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 1, Step = 0.01, Default = 0,
        LegacySetting = "domeRadialCenterDistance",
      },
      new DomeLayerParam {
        Key = "centerSpeed", Label = "Center Speed",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 0.125, Default = 0,
        LegacySetting = "domeRadialCenterSpeed",
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
        LegacySetting = "domeVolumeAnimationSize",
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
        LegacySetting = "domeVolumeRotationSpeed",
      },
      new DomeLayerParam {
        Key = "spacing", Label = "Spacing",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 1, Step = 0.01, Default = 0.1,
        LegacySetting = "domeRadialSize",
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
          LegacySetting = "domeTwinkleDensity",
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
          LegacySetting = "domeRadialSize",
        },
        TwinkleDensityParam,
        new DomeLayerParam {
          Key = "rippleCDStep", Label = "Ripple Cooldown",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 10, Step = 0.1, Default = 1,
          LegacySetting = "domeRippleCDStep",
        },
        new DomeLayerParam {
          Key = "rippleStep", Label = "Ripple Speed",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 4, Step = 0.1, Default = 1,
          LegacySetting = "domeRippleStep",
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
    // Options for the autonomous trigger-source selector: index matches the
    // `source` arg LayerTrigger.Fired takes. Manual = fire only via the Fire
    // button / bound wand button (no autonomous source).
    private static readonly string[] TriggerSourceOptions =
      new string[] { "Manual", "Beat", "Audio" };

    // Ripple's own tuning, independent of the copy still fused inside
    // Paintbrush (docs/layers_inventory.md: twinkle's precedent — the two
    // copies are separate and unremoved until the rest of the disassembly
    // lands). No LegacySetting: a standalone "ripple" layer never existed in
    // pre-layers config, so there is nothing to migrate it from. Firing is
    // driven by LayerTrigger (docs/triggers.md); rippleStep is the playhead
    // expansion speed, unrelated to the trigger.
    internal static readonly DomeLayerParam[] RippleParams = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "rippleStep", Label = "Ripple Speed",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 0.1, Default = 1,
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

    // Stamp's own tuning. No LegacySetting — like Ripple, this is a new
    // standalone layer with no pre-layers global to migrate from (the fused
    // Paintbrush copy used hard-coded constants, not a config knob). Firing is
    // driven by LayerTrigger (docs/triggers.md), defaulting to the Audio source;
    // level/interval tune that source.
    internal static readonly DomeLayerParam[] StampParams = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "trigger", Label = "Trigger",
        Type = DomeLayerParamType.Enum,
        Options = TriggerSourceOptions, Default = 2, // Audio
      },
      TriggerButtonParam,
      TriggerLevelParam,
      TriggerIntervalParam,
    };

    // Metaball's own tuning. No LegacySetting on "contours": the old
    // orientationShowContours global was a bool, and LegacyLayerParamMigration's
    // raw-XML reader only parses numeric elements, so a bool global couldn't
    // have seeded a per-layer param this way even before it was removed — new
    // stacks just default it off.
    internal static readonly DomeLayerParam[] MetaballParams = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "size", Label = "Size",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 0.05, Default = 0.1,
        LegacySetting = "domeRadialSize",
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

    // Background's only tunable: the flat color it paints every pixel.
    internal static readonly DomeLayerParam[] BackgroundParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "color", Label = "Color",
          Type = DomeLayerParamType.Color,
          Min = 0, Max = 0xFFFFFF, Default = 0xFFFFFF,
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

    // Point Cloud's tuning, all visualizer-consumed (read in Visualize()). A
    // new standalone orientation layer with no pre-layers global, so no
    // LegacySetting on any of these. `count` reseeds the spot lattice when it
    // changes; the rest tune the per-frame physics and the drawn spot size.
    internal static readonly DomeLayerParam[] PointCloudParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "count", Label = "Spot Count",
          Type = DomeLayerParamType.Double,
          Min = 4, Max = 160, Step = 1, Default = 48,
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

    // Shooting Star's tuning, all visualizer-consumed (read in Visualize()). A
    // new standalone orientation layer with no pre-layers global, so no
    // LegacySetting on any of these. Dots are born just off the rim and
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

    // Sparkler is Shooting Star in reverse: every trigger births one particle
    // at the current wand/idle aim point and sends it in a random direction at
    // constant speed. The buffer's normal global fade supplies its trail.
    internal static readonly DomeLayerParam[] SparklerParams =
      new DomeLayerParam[] {
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

    // Gyroscope's tuning, all visualizer-consumed (read in Visualize()). A new
    // standalone effect layer with no pre-layers global, so no LegacySetting on
    // any of these. The gimbal motion is driven by device/idle orientation
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

    // Noise Cloud's tuning, all visualizer-consumed (read in Visualize()). A new
    // standalone texture layer with no pre-layers global, so no LegacySetting on
    // any of these. It emits an animated fractal-value-noise field tinting
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
    // field into grains and lets the persistent layer buffer leave short
    // trails. All work is O(dome pixels), independent of apparent density.
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
          Min = -4, Max = 4, Step = 0.125, Default = 1,
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

    // Caustics' tuning, all visualizer-consumed (read in Visualize()). A new
    // standalone texture layer with no pre-layers global, so no LegacySetting on
    // any of these (docs/caustics.md). `method` is the fidelity ladder — three
    // analytic rungs plus the interactive Ripple Tank simulation; a GPU tier
    // would append to Options later without shifting these indices. `scale` is
    // the feature size (wavenumber multiplier; the tank maps it inversely to
    // droplet size), `speed` the churn/advance rate (the tank maps it to sim
    // step rate), `sharpness` the filament thinness (pow exponent — its Max is
    // deliberately modest because filaments thinner than the LED pitch render
    // as sparkle rather than lines), `brightness` the output gain, and `color`
    // the tint (default pale cyan-white: sun through water). The trigger
    // cluster (docs/triggers.md) drives the tank's droplets and only bites in
    // that tier — the analytic tiers have nothing to fire.
    internal static readonly DomeLayerParam[] CausticsParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "method", Label = "Method",
          Type = DomeLayerParamType.Enum,
          Options =
            new string[] { "Shimmer", "Interference", "Lens", "Ripple Tank" },
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
        new DomeLayerParam {
          Key = "wakeSize", Label = "Object Size",
          Type = DomeLayerParamType.Double,
          Min = 0.02, Max = 0.15, Step = 0.005, Default = 0.055,
        },
        new DomeLayerParam {
          Key = "wakeStrength", Label = "Wake Strength",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 1, Step = 0.05, Default = 0.35,
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

  }

  public partial class DomeLayerSettings {

    // The value of one of `layerKey`'s params from the published stack
    // snapshot: the layer's bag value when present, else the descriptor
    // default — so an absent bag/key means the same thing on every surface.
    // Two small array scans per call; visualizers call this once per param per
    // frame (never per pixel), which is noise on the operator thread.
    public static double ParamValue(
      IList<DomeLayerSettings> stack, string layerKey, string paramKey
    ) {
      LayerStackSnapshot snapshot = LayerStackService.SnapshotFor(stack);
      foreach (LayerSnapshot layer in snapshot.Layers) {
        if (layer.RendererId == layerKey &&
            layer.RendererParameters.TryGetValue(
              paramKey, out ParameterValue value)) {
          return value.Value;
        }
      }
      LayerDefinition definition = LayerCatalog.Default.Get(layerKey);
      if (definition != null) {
        foreach (DomeLayerParam parameter in definition.Parameters) {
          if (parameter.Key == paramKey) {
            return parameter.Default;
          }
        }
      }
      return 0;
    }

    // (Blend-mode tunables — the prism family — live on each DomeBlend class's
    // Params, not here: see DomeBlend.Params / DomeBlend.Param.)

    // Allocation-free scan used on the scheduling hot path (visualizer Priority
    // getters): true if the stack has an enabled entry naming `key`. Mirrors the
    // style of Operator.AllInputsEnabled. Safe to call on the operator thread
    // against a published (immutable) stack snapshot.
    public static bool StackActivates(IList<DomeLayerSettings> stack, string key) {
      foreach (LayerSnapshot layer in LayerStackService.SnapshotFor(stack).Layers) {
        if (layer.Enabled && layer.RendererId == key) {
          return true;
        }
      }
      return false;
    }
  }

}

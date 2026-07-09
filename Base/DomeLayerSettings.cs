using System;
using System.Collections.Generic;

namespace Spectrum.Base {

  // How a layer's pixels combine with the composite built from the layers below
  // it. See the blend math in LEDDomeOutputBuffer / the layers design doc.
  //
  // Persisted by name (XSerializer writes the enum member name), so members may
  // be appended freely; do not reorder or rename existing ones. Desaturate and
  // Hue are both adjustment blends: they ignore the source layer's own color
  // and instead reprocess the composite below it (masked by the source's
  // alpha) — into grayscale luma for Desaturate, or into the hue carried up
  // from a hue-publishing layer further below (e.g. Metaball's dedicated
  // `hue` field) for Hue — see CompositeBlend.
  public enum DomeBlendMode { Over, Add, Screen, Lighten, Multiply, Desaturate, Hue }

  // The value type of a per-layer parameter. Values live in the bag as double
  // regardless: Bool is 0/1, Enum is the index into DomeLayerParam.Options,
  // Color is a packed 0xRRGGBB int reinterpreted as a double.
  public enum DomeLayerParamType { Double, Bool, Enum, Color }

  // Static schema for one tunable on a layer (or on a blend mode). The bag on a
  // DomeLayerSettings stores only values keyed by DomeLayerParam.Key; everything
  // else here (range, label, default, which consumer reads it) is compile-time
  // metadata read identically by both UIs, the resolver, and GetParam fallbacks.
  // See ParamsFor / ParamsForBlend for the registry.
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
  public class DomeLayerSettings {
    // Stable string id of the layerable visualizer, e.g. "radial". See
    // DomeLayerVisualizer.LayerKey and LegacyVisKeys below.
    public string VisualizerKey { get; set; }
    public DomeBlendMode BlendMode { get; set; } = DomeBlendMode.Add;
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
    // defaults stay single-sourced in ParamsFor / ParamsForBlend.
    public double GetParam(string key, double fallback) {
      return this.Params != null && this.Params.TryGetValue(key, out double v)
        ? v : fallback;
    }

    // The stack entry naming `key` (or null if none), so a visualizer can read
    // its own params in Visualize(). Allocation-free linear scan of the small
    // immutable snapshot, matching StackActivates; the first match wins (v1
    // disallows duplicate visualizer keys anyway).
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

    // The legacy domeActiveVis int -> layer key mapping, kept so config
    // migration can synthesize a stack from an old file's selector (the
    // domeActiveVis property itself is retired). Index == the old magic int.
    public static readonly string[] LegacyVisKeys = new string[] {
      "volume", "radial", "race", "snakes", "quaternion-test",
      "quaternion-multi-test", "quaternion-paintbrush", "splat", "tv-static",
    };
    // Human-readable labels for the layer visualizer pickers, parallel to
    // LegacyVisKeys (same order). Shared by the native GUI and web UI.
    public static readonly string[] LegacyVisLabels = new string[] {
      "Volume (OG)", "Radial Effects", "Race", "Snakes", "Quaternion Test",
      "Quaternion Multi Test", "Quaternion Paintbrush", "Splat Effect",
      "TV Static",
    };

    // Layerable visualizers that have no legacy domeActiveVis int: split-out
    // Quaternion Paintbrush effects and other new stack primitives. These are
    // only ever appended (never reordered or removed), so the LegacyVisKeys
    // indices that config migration depends on never shift. ExtraLayerLabels
    // is parallel (same order).
    private static readonly string[] ExtraLayerKeys = new string[] {
      "twinkle", "wave", "ripple", "stamp", "metaball", "background",
    };
    private static readonly string[] ExtraLayerLabels = new string[] {
      "Twinkle", "Wave", "Ripple", "Stamp", "Metaball", "Background",
    };

    // The full set of layerable keys/labels offered in the UI pickers: the nine
    // legacy visualizers followed by the extra layers above. The pickers,
    // LabelForKey, and IsLayerKey all draw from this superset; LegacyVisKeys
    // stays exactly the nine so old domeActiveVis values never re-map.
    public static readonly string[] LayerKeys =
      Concat(LegacyVisKeys, ExtraLayerKeys);
    public static readonly string[] LayerLabels =
      Concat(LegacyVisLabels, ExtraLayerLabels);

    private static string[] Concat(string[] a, string[] b) {
      var result = new string[a.Length + b.Length];
      Array.Copy(a, result, a.Length);
      Array.Copy(b, 0, result, a.Length, b.Length);
      return result;
    }

    public static string KeyForLegacyVis(int vis) {
      return vis >= 0 && vis < LegacyVisKeys.Length ? LegacyVisKeys[vis] : null;
    }

    public static string LabelForKey(string key) {
      int i = Array.IndexOf(LayerKeys, key);
      return i >= 0 ? LayerLabels[i] : key;
    }

    // Whether `key` names a layerable visualizer (any picker option). Used to
    // validate incoming stacks; a superset of the legacy nine.
    public static bool IsLayerKey(string key) {
      return key != null && Array.IndexOf(LayerKeys, key) >= 0;
    }

    // ---- Per-layer parameter schemas -------------------------------------
    // Single source of truth for every tunable, keyed the same way LayerKeys /
    // LayerLabels are. Both UIs render editors generically from these, the
    // compositor resolver reads the CompositorConsumed ones, and every GetParam
    // fallback passes the matching Default. Adding a param to a visualizer is one
    // entry here with zero UI code.

    private static readonly DomeLayerParam[] NoParams =
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

    // Radial's tuning, formerly the domeRadial* cluster of global properties.
    private static readonly DomeLayerParam[] RadialParams = new DomeLayerParam[] {
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
    };

    // Volume's tuning (animation size was a MainWindow combo, the speeds were
    // shared globals).
    private static readonly DomeLayerParam[] VolumeParams = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "animationSize", Label = "Animation Size",
        Type = DomeLayerParamType.Double,
        Min = 0, Max = 4, Step = 1, Default = 4,
        LegacySetting = "domeVolumeAnimationSize",
      },
      RotationSpeedParam,
      GradientSpeedParam,
    };

    // Race repurposed two knobs that nominally belonged to other visualizers;
    // per-layer they get honest names.
    private static readonly DomeLayerParam[] RaceParams = new DomeLayerParam[] {
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
    };

    private static readonly DomeLayerParam[] TwinkleParams =
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
    private static readonly DomeLayerParam[] PaintbrushParams =
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
    private static readonly DomeLayerParam[] RippleParams = new DomeLayerParam[] {
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
    private static readonly DomeLayerParam[] StampParams = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "trigger", Label = "Trigger",
        Type = DomeLayerParamType.Enum,
        Options = TriggerSourceOptions, Default = 2, // Audio
      },
      TriggerButtonParam,
      TriggerLevelParam,
      TriggerIntervalParam,
    };

    // Metaball's own tuning, independent of the copy still fused inside
    // Paintbrush (same unremoved-duplicate precedent as Ripple/Stamp). No
    // LegacySetting on "contours": the retired orientationShowContours global
    // was a bool, and LegacyLayerParamMigration's raw-XML reader only parses
    // numeric elements, so a bool global can't seed a per-layer param this
    // way — new stacks just default it off, same as the global's own default.
    private static readonly DomeLayerParam[] MetaballParams = new DomeLayerParam[] {
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
    private static readonly DomeLayerParam[] BackgroundParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "color", Label = "Color",
          Type = DomeLayerParamType.Color,
          Min = 0, Max = 0xFFFFFF, Default = 0xFFFFFF,
        },
      };

    // Visualizer-consumed params for the wave layer: read in Visualize().
    private static readonly DomeLayerParam[] WaveParams = new DomeLayerParam[] {
      new DomeLayerParam {
        Key = "bandWidth", Label = "Band Width",
        Type = DomeLayerParamType.Double,
        Min = 0.02, Max = 0.5, Step = 0.01, Default = 0.12,
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

    // The visualizer-consumed schema for a layer key. Empty for every key that
    // has no tunables.
    public static IReadOnlyList<DomeLayerParam> ParamsFor(string key) {
      switch (key) {
        case "volume":
          return VolumeParams;
        case "radial":
          return RadialParams;
        case "race":
          return RaceParams;
        case "quaternion-paintbrush":
          return PaintbrushParams;
        case "twinkle":
          return TwinkleParams;
        case "wave":
          return WaveParams;
        case "ripple":
          return RippleParams;
        case "stamp":
          return StampParams;
        case "metaball":
          return MetaballParams;
        case "background":
          return BackgroundParams;
        default:
          return NoParams;
      }
    }

    // The value of one of `layerKey`'s params from the published stack
    // snapshot: the layer's bag value when present, else the descriptor
    // default — so an absent bag/key means the same thing on every surface.
    // Two small array scans per call; visualizers call this once per param per
    // frame (never per pixel), which is noise on the operator thread.
    public static double ParamValue(
      IList<DomeLayerSettings> stack, string layerKey, string paramKey
    ) {
      double fallback = 0;
      IReadOnlyList<DomeLayerParam> schema = ParamsFor(layerKey);
      for (int i = 0; i < schema.Count; i++) {
        if (schema[i].Key == paramKey) {
          fallback = schema[i].Default;
          break;
        }
      }
      DomeLayerSettings layer = ForKey(stack, layerKey);
      return layer != null ? layer.GetParam(paramKey, fallback) : fallback;
    }

    // The compositor-consumed schema for a blend mode (params live on the layer
    // that selects the blend, not on any one visualizer). Empty for every blend
    // — Desaturate always runs as grayscale, with no tunables.
    public static IReadOnlyList<DomeLayerParam> ParamsForBlend(DomeBlendMode mode) {
      return NoParams;
    }

    // Allocation-free scan used on the scheduling hot path (visualizer Priority
    // getters): true if the stack has an enabled entry naming `key`. Mirrors the
    // style of Operator.AllInputsEnabled. Safe to call on the operator thread
    // against a published (immutable) stack snapshot.
    public static bool StackActivates(IList<DomeLayerSettings> stack, string key) {
      if (stack == null) {
        return false;
      }
      for (int i = 0; i < stack.Count; i++) {
        DomeLayerSettings layer = stack[i];
        if (layer != null && layer.Enabled && layer.VisualizerKey == key) {
          return true;
        }
      }
      return false;
    }
  }

}

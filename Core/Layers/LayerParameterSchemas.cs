using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spectrum.Base {

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

    // Which named live palette this layer draws its colors from. The stored value
    // is an index into Configuration.domePalettes; UIs replace these generic
    // labels with the current palette names. A 64-entry schema preserves the
    // configured guard rail while the actual list grows and shrinks at runtime.
    private static readonly DomeLayerParam PaletteParam =
      new DomeLayerParam {
        Key = "palette", Label = "Palette",
        Type = DomeLayerParamType.Enum,
        Options = BuildPaletteOptions(),
        Default = 0,
      };

    private static string[] BuildPaletteOptions() {
      var options = new string[PaletteService.MaxPalettes];
      for (int i = 0; i < options.Length; i++) {
        options[i] = "Palette " + (i + 1);
      }
      return options;
    }

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
      PaletteParam,
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
      PaletteParam,
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
      PaletteParam,
    };

    // Splat and Snakes have no other tunables today; the palette picker is
    // their only per-layer param.
    internal static readonly DomeLayerParam[] SplatParams = new DomeLayerParam[] {
      PaletteParam,
    };
    internal static readonly DomeLayerParam[] SnakesParams = new DomeLayerParam[] {
      PaletteParam,
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
      PaletteParam,
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
      PaletteParam,
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
    // other palette-consuming layer: `palette` picks the named palette, and the visualizer
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
      PaletteParam,
      };

    // Watchful Iris turns the full dome into one theatrical eye. The iris
    // pattern is analytic and stable on the dome projection; complexity changes
    // its radial filament count without introducing per-frame noise. The shared
    // orientation center supplies either the spotlighted wand or its idle drift,
    // constrained to a natural gaze range inside the sclera. Capture level
    // dilates the pupil. The manual action always blinks, while blinkTrigger
    // adds a beat or strong-audio source. Eyelid softness controls the resting
    // almond edge and closing lid; sclera brightness scales the completed white,
    // blush, and vascular surface without changing the iris or eyelids.
    internal static readonly DomeLayerParam[] WatchfulIrisParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "irisComplexity", Label = "Iris Complexity",
          Type = DomeLayerParamType.Double,
          Min = 3, Max = 32, Step = 1, Default = 14,
        },
        new DomeLayerParam {
          Key = "pupilSize", Label = "Pupil Size",
          Type = DomeLayerParamType.Double,
          Min = 0.08, Max = 0.65, Step = 0.01, Default = 0.28,
        },
        new DomeLayerParam {
          Key = "dilationGain", Label = "Dilation Gain",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 0.8, Step = 0.01, Default = 0.28,
        },
        new DomeLayerParam {
          Key = "blinkTrigger", Label = "Blink Trigger",
          Type = DomeLayerParamType.Enum,
          Options = new string[] { "Manual", "Beat", "Audio Transient" },
          Default = 2,
        },
        new DomeLayerParam {
          Key = "eyelidSoftness", Label = "Eyelid Softness",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 0.18, Step = 0.005, Default = 0.035,
        },
        new DomeLayerParam {
          Key = "scleraBrightness", Label = "Sclera Brightness",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 2, Step = 0.05, Default = 1,
        },
      PaletteParam,
      };

    // Living Skin evolves a persistent two-chemical Gray-Scott field directly
    // over the dome topology's shared spatial-neighbor table. Feed and kill
    // select the morphology, while diffusionScale chooses which baked neighbor
    // radius carries chemicals between physical LEDs. Simulation speed changes
    // the fixed-step cadence without changing the numerical model. The initial
    // field and every injected patch are deterministic, so duplicate instances
    // start alike but retain independent state. Manual Fire always injects a
    // patch; seedSource optionally adds beat-boundary injections. Clear removes
    // the second chemical and leaves the surface dormant until it is seeded.
    // Held wand buttons continuously apply feed, poison, or erase chemistry at
    // the aimed surface pixel. Brush radius selects bounded baked-neighbor
    // rings, and strength controls how quickly a held brush reaches its target.
    internal static readonly DomeLayerParam[] LivingSkinParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "feedRate", Label = "Feed Rate",
          Type = DomeLayerParamType.Double,
          Min = 0.01, Max = 0.09, Step = 0.0005, Default = 0.0367,
        },
        new DomeLayerParam {
          Key = "killRate", Label = "Kill Rate",
          Type = DomeLayerParamType.Double,
          Min = 0.03, Max = 0.08, Step = 0.0005, Default = 0.0649,
        },
        new DomeLayerParam {
          Key = "diffusionScale", Label = "Diffusion Scale",
          Type = DomeLayerParamType.Double,
          Min = 1, Max = 4, Step = 1, Default = 2,
        },
        new DomeLayerParam {
          Key = "simulationSpeed", Label = "Simulation Speed",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 4, Step = 0.05, Default = 1,
        },
        new DomeLayerParam {
          Key = "seedSource", Label = "Seed Source",
          Type = DomeLayerParamType.Enum,
          Options = new string[] { "Initial + Manual", "Beat + Manual" },
          Default = 1,
        },
        new DomeLayerParam {
          Key = "edgeContrast", Label = "Edge Contrast",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 8, Step = 0.1, Default = 3,
        },
        new DomeLayerParam {
          Key = "feedButton", Label = "Feed Button",
          Type = DomeLayerParamType.Enum,
          Options = new string[] { "Unbound", "1", "2", "3" },
          Default = 1,
        },
        new DomeLayerParam {
          Key = "poisonButton", Label = "Poison Button",
          Type = DomeLayerParamType.Enum,
          Options = new string[] { "Unbound", "1", "2", "3" },
          Default = 2,
        },
        new DomeLayerParam {
          Key = "eraseButton", Label = "Erase Button",
          Type = DomeLayerParamType.Enum,
          Options = new string[] { "Unbound", "1", "2", "3" },
          Default = 3,
        },
        new DomeLayerParam {
          Key = "brushRadius", Label = "Brush Radius",
          Type = DomeLayerParamType.Double,
          Min = 1, Max = 4, Step = 1, Default = 2,
        },
        new DomeLayerParam {
          Key = "brushStrength", Label = "Brush Strength",
          Type = DomeLayerParamType.Double,
          Min = 0.05, Max = 1, Step = 0.05, Default = 0.35,
        },
      PaletteParam,
      };

    // Arc Lightning routes every strike over the physical dome graph rather
    // than treating the LEDs as a flat texture. Jaggedness randomizes positive
    // strut costs before the shortest route is selected, preserving a connected
    // origin-to-destination bolt while allowing crooked alternatives. Branches
    // are short graph walks off that main route. Width expands the energized
    // struts through the shared spatial-neighbor table; afterglow is the
    // brightness half-life after the live strike passes. Manual Fire and Clear
    // remain available regardless of the selected autonomous trigger.
    internal static readonly DomeLayerParam[] ArcLightningParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "branchCount", Label = "Branch Count",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 12, Step = 1, Default = 4,
        },
        new DomeLayerParam {
          Key = "jaggedness", Label = "Jaggedness",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 1, Step = 0.05, Default = 0.65,
        },
        new DomeLayerParam {
          Key = "width", Label = "Width",
          Type = DomeLayerParamType.Double,
          Min = 1, Max = 4, Step = 1, Default = 2,
        },
        new DomeLayerParam {
          Key = "afterglow", Label = "Afterglow (s)",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 3, Step = 0.05, Default = 0.4,
        },
        new DomeLayerParam {
          Key = "duration", Label = "Strike Duration (s)",
          Type = DomeLayerParamType.Double,
          Min = 0.05, Max = 1.5, Step = 0.05, Default = 0.25,
        },
        new DomeLayerParam {
          Key = "trigger", Label = "Trigger",
          Type = DomeLayerParamType.Enum,
          Options = TriggerSourceOptions, Default = 1, // Beat
        },
        TriggerButtonParam,
        TriggerLevelParam,
        TriggerIntervalParam,
      PaletteParam,
      };

    // Glass Mosaic treats the deployed triangular faces as persistent color
    // cells. A trigger starts at the current wand aim (or a deterministic
    // fallback), rotates the starting connected tile group, then advances over
    // shared-edge face adjacency. Grouping controls how many connected tiles
    // change together; the propagation rule changes neighbor ordering without
    // breaking connectivity. Because the physical dome carries LEDs on struts
    // rather than face interiors, border brightness is the resting intensity
    // of the shared stained-glass edges and cascade arrivals pulse above it.
    // The optional flip transition narrows the old tile face to edge-on before
    // opening the new palette phase; Instant preserves existing configurations.
    internal static readonly DomeLayerParam[] GlassMosaicParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "tileGrouping", Label = "Tile Grouping",
          Type = DomeLayerParamType.Double,
          Min = 1, Max = 6, Step = 1, Default = 1,
        },
        new DomeLayerParam {
          Key = "cascadeSpeed", Label = "Cascade Speed (tiles/s)",
          Type = DomeLayerParamType.Double,
          Min = 1, Max = 120, Step = 1, Default = 30,
        },
        new DomeLayerParam {
          Key = "propagationRule", Label = "Propagation Rule",
          Type = DomeLayerParamType.Enum,
          Options = new string[] {
            "Neighbor Wave", "Clockwise Wave", "Random Domino",
          },
          Default = 0,
        },
        new DomeLayerParam {
          Key = "borderBrightness", Label = "Border Brightness",
          Type = DomeLayerParamType.Double,
          Min = 0.02, Max = 1, Step = 0.02, Default = 0.18,
        },
        new DomeLayerParam {
          Key = "tileTransition", Label = "Tile Transition",
          Type = DomeLayerParamType.Enum,
          Options = new string[] { "Instant", "Flip" }, Default = 0,
        },
        new DomeLayerParam {
          Key = "trigger", Label = "Trigger",
          Type = DomeLayerParamType.Enum,
          Options = TriggerSourceOptions, Default = 1, // Beat
        },
        TriggerButtonParam,
        TriggerLevelParam,
        TriggerIntervalParam,
      PaletteParam,
      };

    // Cellular Dome assigns one binary automaton cell to every discovered
    // triangular face. Shared Edges uses the three face neighbors across the
    // triangle's struts; Shared Vertices expands the neighborhood to every
    // face touching any of its corners. The four rule families deliberately
    // cover stable colonies, exact-one oscillation, persistent traveling
    // fronts, and a more volatile birth pattern on either topology. Timed mode
    // advances at generationRate; Beat Step advances once per beat; Beat Rule
    // Cycle also rotates to the next rule on each beat. Fire injects a colony,
    // Clear empties the field, and held wand buttons 1/2/3 seed, erase, and
    // mutate the aimed cells respectively. Birth Color is relative to the
    // selected palette; surviving cells age through subsequent slots as
    // their brightness decays.
    internal static readonly DomeLayerParam[] CellularDomeParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "rule", Label = "Rule",
          Type = DomeLayerParamType.Enum,
          Options = new string[] {
            "Colonies (B2/S12)", "Oscillators (B1/S0)",
            "Traveling Fronts", "Chaos (B13/S12)",
          },
          Default = 0,
        },
        new DomeLayerParam {
          Key = "neighborhood", Label = "Neighborhood",
          Type = DomeLayerParamType.Enum,
          Options = new string[] { "Shared Edges", "Shared Vertices" },
          Default = 0,
        },
        new DomeLayerParam {
          Key = "generationRate", Label = "Generation Rate (gen/s)",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 30, Step = 0.25, Default = 6,
        },
        new DomeLayerParam {
          Key = "birthColor", Label = "Birth Color",
          Type = DomeLayerParamType.Enum,
          Options = new string[] {
            "Color 1", "Color 2", "Color 3", "Color 4",
            "Color 5", "Color 6", "Color 7", "Color 8",
          },
          Default = 0,
        },
        new DomeLayerParam {
          Key = "ageDecay", Label = "Age Decay (s)",
          Type = DomeLayerParamType.Double,
          Min = 0.1, Max = 12, Step = 0.1, Default = 2.5,
        },
        new DomeLayerParam {
          Key = "triggerMode", Label = "Trigger Mode",
          Type = DomeLayerParamType.Enum,
          Options = new string[] {
            "Timed", "Beat Step", "Beat Rule Cycle",
          },
          Default = 0,
        },
      PaletteParam,
      };

    // Firefly Swarm keeps a bounded, persistent boid flock on the true dome
    // hemisphere. Cohesion and separation shape group motion while wander
    // keeps the flock alive without input. Every active wand contributes its
    // aim as either an attractor or repeller; a capture-volume rise above its
    // recent envelope briefly startles the flock outward. Trail length is an
    // independent rendered-light half-life rather than the global dome fade.
    internal static readonly DomeLayerParam[] FireflySwarmParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "population", Label = "Population",
          Type = DomeLayerParamType.Double,
          Min = 8, Max = 160, Step = 1, Default = 48,
        },
        new DomeLayerParam {
          Key = "cohesion", Label = "Cohesion",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 4, Step = 0.05, Default = 1.2,
        },
        new DomeLayerParam {
          Key = "separation", Label = "Separation",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 4, Step = 0.05, Default = 1.8,
        },
        new DomeLayerParam {
          Key = "wander", Label = "Wander",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 4, Step = 0.05, Default = 0.65,
        },
        new DomeLayerParam {
          Key = "interactionMode", Label = "Wand Interaction",
          Type = DomeLayerParamType.Enum,
          Options = new string[] { "Attract", "Repel" }, Default = 0,
        },
        new DomeLayerParam {
          Key = "dotSize", Label = "Dot Size",
          Type = DomeLayerParamType.Double,
          Min = 0.015, Max = 0.18, Step = 0.005, Default = 0.055,
        },
        new DomeLayerParam {
          Key = "trailLength", Label = "Trail Half-Life (s)",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 3, Step = 0.05, Default = 0.45,
        },
      PaletteParam,
      };

    // Rain Chamber keeps a bounded set of crown-born droplets on the true
    // dome hemisphere. Capture volume scales their spawn rate; tangent gravity
    // pulls them toward the rim while moving wand aims form local umbrella,
    // dry-region, or motion-driven wind fields. Trail retention is the
    // rendered-light half-life and rim impacts emit short-lived expanding
    // splash rings.
    internal static readonly DomeLayerParam[] RainChamberParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "rainfallRate", Label = "Rainfall Rate (drops/s)",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 80, Step = 1, Default = 22,
        },
        new DomeLayerParam {
          Key = "gravity", Label = "Spherical Gravity",
          Type = DomeLayerParamType.Double,
          Min = 0.1, Max = 4, Step = 0.05, Default = 1.4,
        },
        new DomeLayerParam {
          Key = "dropletSize", Label = "Droplet Size",
          Type = DomeLayerParamType.Double,
          Min = 0.015, Max = 0.14, Step = 0.005, Default = 0.045,
        },
        new DomeLayerParam {
          Key = "trailRetention", Label = "Trail Half-Life (s)",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 3, Step = 0.05, Default = 0.7,
        },
        new DomeLayerParam {
          Key = "interactionMode", Label = "Wand Interaction",
          Type = DomeLayerParamType.Enum,
          Options = new string[] { "Umbrella", "Dry Region", "Wind" },
          Default = 0,
        },
        new DomeLayerParam {
          Key = "wind", Label = "Wand Strength",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 4, Step = 0.05, Default = 1.25,
        },
        new DomeLayerParam {
          Key = "splashStrength", Label = "Splash Strength",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 2, Step = 0.05, Default = 0.9,
        },
      PaletteParam,
      };

    // Topographic Dream samples a seamless evolving elevation field directly
    // on the true dome surface. Bright interval contours and the current
    // coastline sit over subdued land/water fills. Capture volume raises the
    // configured sea level, and optional orientation binding rotates the
    // whole landscape through the shared wand/idle center.
    internal static readonly DomeLayerParam[] TopographicDreamParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "terrainScale", Label = "Terrain Scale",
          Type = DomeLayerParamType.Double,
          Min = 0.5, Max = 6, Step = 0.1, Default = 2.2,
        },
        new DomeLayerParam {
          Key = "evolutionSpeed", Label = "Evolution Speed",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 1.5, Step = 0.02, Default = 0.12,
        },
        new DomeLayerParam {
          Key = "contourInterval", Label = "Contour Interval",
          Type = DomeLayerParamType.Double,
          Min = 0.04, Max = 0.3, Step = 0.01, Default = 0.11,
        },
        new DomeLayerParam {
          Key = "lineWidth", Label = "Line Width",
          Type = DomeLayerParamType.Double,
          Min = 0.02, Max = 0.45, Step = 0.01, Default = 0.14,
        },
        new DomeLayerParam {
          Key = "seaLevel", Label = "Quiet Sea Level",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 1, Step = 0.01, Default = 0.42,
        },
        new DomeLayerParam {
          Key = "bindOrientation", Label = "Follow Orientation",
          Type = DomeLayerParamType.Bool, Default = 0,
        },
      PaletteParam,
      };

    // Orbital Garden keeps a bounded deterministic collection of luminous
    // bodies on the true dome hemisphere. Every connected wand contributes a
    // colored gravity well; a fixed fallback well sustains stable motion when
    // no hardware is present. Gravity is projected into each body's tangent
    // plane, damping controls how long transferred orbital energy survives,
    // and collision behavior selects a bounce, a luminous bloom, or a bloom
    // with finite-lived fragments. Trail length is an independent rendered-
    // light half-life rather than the global dome fade.
    internal static readonly DomeLayerParam[] OrbitalGardenParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "bodyCount", Label = "Body Count",
          Type = DomeLayerParamType.Double,
          Min = 4, Max = 96, Step = 1, Default = 28,
        },
        new DomeLayerParam {
          Key = "gravity", Label = "Gravity",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 5, Step = 0.05, Default = 1.6,
        },
        new DomeLayerParam {
          Key = "orbitalDamping", Label = "Orbital Damping",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 3, Step = 0.05, Default = 0.12,
        },
        new DomeLayerParam {
          Key = "collisionBehavior", Label = "Collisions",
          Type = DomeLayerParamType.Enum,
          Options = new string[] {
            "Bounce", "Bloom", "Fragment Bloom",
          },
          Default = 2,
        },
        new DomeLayerParam {
          Key = "trailLength", Label = "Trail Half-Life (s)",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 4, Step = 0.05, Default = 0.8,
        },
        new DomeLayerParam {
          Key = "bodySize", Label = "Body Size",
          Type = DomeLayerParamType.Double,
          Min = 0.015, Max = 0.16, Step = 0.005, Default = 0.05,
        },
      PaletteParam,
      };

    // Lava Lamp Sky keeps a small persistent set of large, soft bodies on the
    // true dome hemisphere. Their density changes with heat, so warm bodies
    // climb the spherical gravity field while cool ones sink toward the rim.
    // Surface tension draws nearby bodies into shared silhouettes and restores
    // their roundness; viscosity gives the motion its deliberately heavy pace.
    // Capture volume raises heat and buoyancy while encouraging pinched bodies
    // to separate. Optional gravity binding tilts the rise axis with the shared
    // wand/idle orientation instead of fixing it at the crown.
    internal static readonly DomeLayerParam[] LavaLampSkyParams =
      new DomeLayerParam[] {
        new DomeLayerParam {
          Key = "blobCount", Label = "Blob Count",
          Type = DomeLayerParamType.Double,
          Min = 3, Max = 24, Step = 1, Default = 9,
        },
        new DomeLayerParam {
          Key = "viscosity", Label = "Viscosity",
          Type = DomeLayerParamType.Double,
          Min = 0.2, Max = 4, Step = 0.05, Default = 1.8,
        },
        new DomeLayerParam {
          Key = "buoyancy", Label = "Buoyancy",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 3, Step = 0.05, Default = 0.8,
        },
        new DomeLayerParam {
          Key = "surfaceTension", Label = "Surface Tension",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 3, Step = 0.05, Default = 1.35,
        },
        new DomeLayerParam {
          Key = "heat", Label = "Heat",
          Type = DomeLayerParamType.Double,
          Min = 0, Max = 1, Step = 0.01, Default = 0.35,
        },
        new DomeLayerParam {
          Key = "bindGravity", Label = "Follow Orientation",
          Type = DomeLayerParamType.Bool, Default = 1,
        },
      PaletteParam,
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

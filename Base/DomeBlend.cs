using System;
using System.Collections.Generic;

namespace Spectrum.Base {

  // Everything a blend needs to run, handed to DomeBlend.Blend once per layer
  // per frame by the compositor (LEDDomeOutput.Composite). A readonly struct so
  // the per-frame path allocates nothing.
  public readonly struct DomeBlendContext {
    // The composite built from the layers below; the blend mutates this.
    public LEDDomeOutputBuffer Dest { get; }
    // The blending layer's own buffer (index-aligned with Dest). Never mutated.
    public LEDDomeOutputBuffer Src { get; }
    // Pre-pass copy of Dest, taken by the compositor immediately before this
    // blend runs — non-null exactly when the blend declares NeedsSnapshot.
    // Spatial blends read neighbors from here so the effect never smears
    // order-dependently along the pixel array they are mutating.
    public LEDDomeOutputBuffer Snapshot { get; }
    // The stack entry that selected this blend, for reading its Params bag.
    // The published stack is an immutable snapshot, so aliasing it is safe.
    public DomeLayerSettings Settings { get; }
    // The layer's opacity, 0..1, applied before the blend.
    public double Opacity { get; }
    // Wall-clock seconds accumulated by the compositor, driving the
    // time-varying prism params (spin). Frame-rate independent.
    public double Seconds { get; }
    // Live wand angle source for the prism blends' "Follow Orientation"
    // option (docs/prism.md). Nullable — a dome wired up without an
    // orientation source simply never follows.
    public OrientationAngleProvider Orientation { get; }

    public DomeBlendContext(
      LEDDomeOutputBuffer dest, LEDDomeOutputBuffer src,
      LEDDomeOutputBuffer snapshot, DomeLayerSettings settings,
      double opacity, double seconds, OrientationAngleProvider orientation
    ) {
      this.Dest = dest;
      this.Src = src;
      this.Snapshot = snapshot;
      this.Settings = settings;
      this.Opacity = opacity;
      this.Seconds = seconds;
      this.Orientation = orientation;
    }

    // Resolve a prism blend's effective angle. When `follow` is set and a wand
    // is actually the orientation center, the wand supplies the angle entirely.
    // Otherwise (follow off, or no wand available) the angle is driven purely
    // by the time-varying `spin` (turns/sec) — so spin 0 holds a fixed axis
    // and any nonzero spin sweeps through every angle.
    public double PrismAngle(double spin, bool follow) {
      if (follow && this.Orientation != null &&
          this.Orientation.TryGetAngle(out double wandAngle)) {
        return wandAngle;
      }
      return 2 * Math.PI * spin * this.Seconds;
    }
  }

  // One blend mode: how a layer's pixels combine with the composite built from
  // the layers below it. Replaces the old DomeBlendMode enum — each mode is a
  // singleton class owning its identity, its compositor-consumed param schema,
  // and its per-frame math, so adding a mode is one class plus a registry
  // entry (no switch statements to extend).
  //
  // Layers persist their blend by Name (DomeLayerSettings.BlendMode, written
  // verbatim into config/scene XML — the same strings the retired enum
  // serialized), so Names are frozen: never rename one, and give new blends
  // new names.
  //
  // Desaturate and Hue are adjustment blends: they ignore the source layer's
  // own color and instead reprocess the composite below it (masked by the
  // source's alpha). ChromaticFringe, EdgeSpectrum and Iridescence are the
  // prism family (docs/prism.md): also adjustment blends, but the first two
  // are *spatial* — they resample the composite through the baked neighbor
  // table, so they declare NeedsSnapshot and read the pre-pass copy. All
  // three carry compositor-consumed tunables via Params.
  public abstract class DomeBlend {

    // Stable identity: persisted in config/scene files, carried on the web
    // wire, and shown in both UIs' pickers.
    public abstract string Name { get; }

    // Compositor-consumed tunables this blend reads from the selecting layer's
    // Params bag (never read by a visualizer). Empty for the plain blends.
    public virtual IReadOnlyList<DomeLayerParam> Params => NoParams;

    // True if the compositor must snapshot the composite into ctx.Snapshot
    // before this blend runs (i.e. the blend samples neighbors of the buffer
    // it mutates).
    public virtual bool NeedsSnapshot => false;

    // Blend ctx.Src into ctx.Dest. Called once per layer per frame on the
    // operator thread; implementations own their per-pixel loop and mutate
    // pixels only through the single-repack channel ops on LEDDomeOutputPixel.
    public abstract void Blend(in DomeBlendContext ctx);

    // The value of one of this blend's params, resolved the same way
    // DomeLayerSettings.ParamValue resolves a visualizer param: the layer's
    // bag value when present, else this blend's descriptor default. Called
    // once per param per frame (never per pixel).
    public double Param(DomeLayerSettings layer, string paramKey) {
      double fallback = 0;
      IReadOnlyList<DomeLayerParam> schema = this.Params;
      for (int i = 0; i < schema.Count; i++) {
        if (schema[i].Key == paramKey) {
          fallback = schema[i].Default;
          break;
        }
      }
      return layer != null ? layer.GetParam(paramKey, fallback) : fallback;
    }

    // Both UIs show blends by name (the native ComboBox displayed the enum's
    // ToString; keep that contract).
    public override string ToString() {
      return this.Name;
    }

    protected static readonly DomeLayerParam[] NoParams =
      Array.Empty<DomeLayerParam>();

    // ---- Registry ----------------------------------------------------------
    // The singleton instances, in the order the pickers list them (the old
    // enum's order). Only ever append; Names are persisted.

    public static readonly DomeBlend Over = new OverBlend();
    public static readonly DomeBlend Add = new AddBlend();
    public static readonly DomeBlend Screen = new ScreenBlend();
    public static readonly DomeBlend Lighten = new LightenBlend();
    public static readonly DomeBlend Multiply = new MultiplyBlend();
    public static readonly DomeBlend Desaturate = new DesaturateBlend();
    public static readonly DomeBlend Hue = new HueBlend();
    public static readonly DomeBlend ChromaticFringe = new ChromaticFringeBlend();
    public static readonly DomeBlend EdgeSpectrum = new EdgeSpectrumBlend();
    public static readonly DomeBlend Iridescence = new IridescenceBlend();

    public static readonly IReadOnlyList<DomeBlend> All = new DomeBlend[] {
      Over, Add, Screen, Lighten, Multiply, Desaturate, Hue,
      ChromaticFringe, EdgeSpectrum, Iridescence,
    };

    // The blend a fresh layer gets (the old enum default).
    public static DomeBlend Default => Add;

    // The registered blend named `name`, or null if unknown. A ten-element
    // scan; callers cache the result (ResolvedLayer, the UIs' row models) so
    // this never runs per frame.
    public static DomeBlend FromName(string name) {
      if (name == null) {
        return null;
      }
      for (int i = 0; i < All.Count; i++) {
        if (All[i].Name == name) {
          return All[i];
        }
      }
      return null;
    }
  }
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Spectrum.Base {

  [Flags]
  public enum CompositeRequirements {
    None = 0,
    ReadsSourceColor = 1,
    ReadsSourceMask = 2,
    ReadsDestination = 4,
    ReadsDestinationNeighbors = 8,
    PublishesHue = 16,
    ReadsOrientation = 32,
    ReadsHistory = 64,
  }

  public interface ICompositeOptions { }

  public sealed record EmptyCompositeOptions : ICompositeOptions {
    public static EmptyCompositeOptions Instance { get; } = new();
  }

  public sealed record ChromaticFringeOptions(
    double Offset, double Spin, bool FollowOrientation
  ) : ICompositeOptions;
  public sealed record EdgeSpectrumOptions(
    double Strength, double Offset
  ) : ICompositeOptions;
  public sealed record RefractOptions(double Strength) : ICompositeOptions;
  public sealed record IridescenceOptions(
    double Strength, double Bands, double Spin, bool FollowOrientation
  ) : ICompositeOptions;

  public interface ICompositeOperation {
    string Id { get; }
    CompositeRequirements Requirements { get; }
    ICompositeOptions CompileOptions(
      ImmutableDictionary<string, ParameterValue> parameters);
    void Execute(in DomeBlendContext context);
  }

  // Everything a blend needs to run, handed to DomeBlend.Blend once per layer
  // per frame by the compositor (LEDDomeOutput.Composite). A readonly struct so
  // the per-frame path allocates nothing.
  public readonly struct DomeBlendContext {
    // The composite built from the layers below; the blend mutates this.
    public DomeFrame Dest { get; }
    // The blending layer's own buffer (index-aligned with Dest). Never mutated.
    public DomeFrame Src { get; }
    // Pre-pass copy of Dest, taken by the compositor immediately before this
    // blend runs — non-null when requirements include destination neighbors.
    // Spatial blends read neighbors from here so the effect never smears
    // order-dependently along the pixel array they are mutating.
    public DomeFrame Snapshot { get; }
    // Typed, validated operation options compiled when the stack changed.
    public ICompositeOptions Options { get; }
    // The layer's opacity, 0..1, applied before the blend.
    public double Opacity { get; }
    // Wall-clock seconds accumulated by the compositor, driving the
    // time-varying prism params (spin). Frame-rate independent.
    public double Seconds { get; }
    // Live wand angle source for the prism blends' "Follow Orientation"
    // option (docs/prism.md). Nullable — a dome wired up without an
    // orientation source simply never follows.
    public OrientationAngleProvider Orientation { get; }
    // Per-layer retained composite frames for stateful adjustment operations.
    // Null unless the operation declares ReadsHistory.
    public CompositeFrameHistory History { get; }
    // Resolve one color from a configured named palette at a normalized
    // position. Hosts without a palette service may leave this null; palette-
    // aware operations then preserve the sampled composite's color.
    public Func<int, double, int> PaletteColor { get; }

    public DomeBlendContext(
      DomeFrame dest, DomeFrame src, DomeFrame snapshot,
      ICompositeOptions options,
      double opacity, double seconds, OrientationAngleProvider orientation,
      CompositeFrameHistory history = null,
      Func<int, double, int> paletteColor = null
    ) {
      this.Dest = dest ?? throw new ArgumentNullException(nameof(dest));
      this.Src = src ?? throw new ArgumentNullException(nameof(src));
      if (!ReferenceEquals(dest.Topology, src.Topology)) {
        throw new ArgumentException(
          "Composite frames must share one topology.", nameof(src));
      }
      if (snapshot != null &&
          !ReferenceEquals(dest.Topology, snapshot.Topology)) {
        throw new ArgumentException(
          "Composite snapshots must share the destination topology.",
          nameof(snapshot));
      }
      this.Snapshot = snapshot;
      this.Options = options ?? EmptyCompositeOptions.Instance;
      this.Opacity = opacity;
      this.Seconds = seconds;
      this.Orientation = orientation;
      this.History = history;
      this.PaletteColor = paletteColor;
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
  // Layers persist their blend by Id (DomeLayerSettings.BlendMode, written
  // verbatim into config/scene XML — the same strings the retired enum
  // serialized), so IDs are frozen: never rename one, and give new blends
  // new IDs.
  //
  // Desaturate and Hue are adjustment blends: they ignore the source layer's
  // own color and instead reprocess the composite below it (masked by the
  // source's alpha). ChromaticFringe, EdgeSpectrum, Iridescence and Refract
  // are the prism family (docs/prism.md, docs/caustics.md): also adjustment
  // blends, but ChromaticFringe, EdgeSpectrum and Refract are *spatial* —
  // they resample the composite through the baked neighbor table, so they
  // declare destination-neighbor requirements and read the pre-pass copy.
  // They expose compositor-consumed tunable descriptors via Params.
  public abstract class DomeBlend : ICompositeOperation {

    // Stable identity: persisted in config/scene files, carried on the web
    // wire, and shown in both UIs' pickers.
    public abstract string Id { get; }
    public virtual string DisplayName => this.Id;

    // Compositor-consumed tunables this operation reads from the selecting
    // layer's operation parameter bag. Empty for the plain blends.
    public virtual IReadOnlyList<DomeLayerParam> Params => NoParams;

    public virtual CompositeRequirements Requirements =>
      CompositeRequirements.ReadsSourceColor |
      CompositeRequirements.ReadsDestination;

    public virtual ICompositeOptions CompileOptions(
      ImmutableDictionary<string, ParameterValue> parameters
    ) => this.Params.Count == 0
      ? EmptyCompositeOptions.Instance
      : throw new InvalidOperationException(
          "Blend " + this.Id + " must compile typed options.");

    // Blend ctx.Src into ctx.Dest. Called once per layer per frame on the
    // operator thread; implementations own their per-pixel loop and mutate
    // pixels only through the single-repack channel ops on LEDDomeOutputPixel.
    public abstract void Blend(in DomeBlendContext ctx);

    public void Execute(in DomeBlendContext context) => this.Blend(context);

    // Both UIs show blends by name (the native ComboBox displayed the enum's
    // ToString; keep that contract).
    public override string ToString() {
      return this.DisplayName;
    }

    protected static readonly DomeLayerParam[] NoParams =
      Array.Empty<DomeLayerParam>();

    // ---- Registry ----------------------------------------------------------
    // The singleton instances, in the order the pickers list them (the old
    // enum's order). Only ever append; IDs are persisted.

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
    public static readonly DomeBlend Refract = new RefractBlend();

    public static readonly IReadOnlyList<DomeBlend> All = new DomeBlend[] {
      Over, Add, Screen, Lighten, Multiply, Desaturate, Hue,
      ChromaticFringe, EdgeSpectrum, Iridescence, Refract,
    };

    // The blend a fresh layer gets (the old enum default).
    public static DomeBlend Default => Add;

    // The registered blend identified by `id`, or null if unknown. A scan of the
    // small registry; callers cache the result (render plans, the UIs' row
    // models) so this never runs per frame.
    public static DomeBlend FromId(string id) {
      if (id == null) {
        return null;
      }
      for (int i = 0; i < All.Count; i++) {
        if (All[i].Id == id) {
          return All[i];
        }
      }
      return null;
    }
  }
}

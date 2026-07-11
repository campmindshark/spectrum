using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using System.ComponentModel;

namespace Spectrum.LEDs {

  enum LEDDomeStrutTypes { Yellow, Red, Blue, Green, Purple, Orange };

  public class LEDDomeOutput : Output {

    // There are 8 strands coming out of each control box. For each of these
    // strands, this variable represents the sequence of strut types
    private static readonly LEDDomeStrutTypes[][] controlBoxStrutOrder =
      new LEDDomeStrutTypes[][] {
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Green, LEDDomeStrutTypes.Blue,
          LEDDomeStrutTypes.Orange, LEDDomeStrutTypes.Orange,
          LEDDomeStrutTypes.Yellow,
        },
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Orange, LEDDomeStrutTypes.Blue,
          LEDDomeStrutTypes.Purple, LEDDomeStrutTypes.Blue,
          LEDDomeStrutTypes.Red,
        },
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Red, LEDDomeStrutTypes.Blue,
          LEDDomeStrutTypes.Green, LEDDomeStrutTypes.Green,
          LEDDomeStrutTypes.Blue,
        },
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Green, LEDDomeStrutTypes.Blue,
          LEDDomeStrutTypes.Red, LEDDomeStrutTypes.Yellow,
          LEDDomeStrutTypes.Yellow,
        },
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Green, LEDDomeStrutTypes.Purple,
          LEDDomeStrutTypes.Blue, LEDDomeStrutTypes.Red,
        },
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Green, LEDDomeStrutTypes.Purple,
          LEDDomeStrutTypes.Purple, LEDDomeStrutTypes.Green,
          LEDDomeStrutTypes.Green,
        },
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Orange, LEDDomeStrutTypes.Yellow,
          LEDDomeStrutTypes.Yellow, LEDDomeStrutTypes.Red,
          LEDDomeStrutTypes.Red,
        },
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Blue, LEDDomeStrutTypes.Blue,
          LEDDomeStrutTypes.Blue, LEDDomeStrutTypes.Yellow,
        },
      };
    private static readonly Dictionary<LEDDomeStrutTypes, int> strutLengths =
      new Dictionary<LEDDomeStrutTypes, int> {
        [LEDDomeStrutTypes.Yellow] = 34,
        [LEDDomeStrutTypes.Red] = 40,
        [LEDDomeStrutTypes.Blue] = 40,
        [LEDDomeStrutTypes.Orange] = 40,
        [LEDDomeStrutTypes.Green] = 42,
        [LEDDomeStrutTypes.Purple] = 44,
      };
    // We assign each strut an index. This variable maps that strut index to
    // a control box, and an index within that control box. The control box
    // index is based on the sequence of the 8 strands and the struts that
    // comprise them. See comment at the top of FindStrutIndex for more details.
    private static readonly Tuple<int, int>[] strutPositions = new Tuple<int, int>[] {
      new Tuple<int, int>(0, 22), new Tuple<int, int>(0, 23),
      new Tuple<int, int>(1, 36), new Tuple<int, int>(1, 21),
      new Tuple<int, int>(1, 22), new Tuple<int, int>(1, 23),
      new Tuple<int, int>(2, 36), new Tuple<int, int>(2, 21),
      new Tuple<int, int>(2, 22), new Tuple<int, int>(2, 23),
      new Tuple<int, int>(3, 36), new Tuple<int, int>(3, 21),
      new Tuple<int, int>(3, 22), new Tuple<int, int>(3, 23),
      new Tuple<int, int>(4, 36), new Tuple<int, int>(4, 21),
      new Tuple<int, int>(4, 22), new Tuple<int, int>(4, 23),
      new Tuple<int, int>(0, 36), new Tuple<int, int>(0, 21),
      new Tuple<int, int>(0, 5), new Tuple<int, int>(0, 19),
      new Tuple<int, int>(1, 30), new Tuple<int, int>(1, 29),
      new Tuple<int, int>(1, 5), new Tuple<int, int>(1, 19),
      new Tuple<int, int>(2, 30), new Tuple<int, int>(2, 29),
      new Tuple<int, int>(2, 5), new Tuple<int, int>(2, 19),
      new Tuple<int, int>(3, 30), new Tuple<int, int>(3, 29),
      new Tuple<int, int>(3, 5), new Tuple<int, int>(3, 19),
      new Tuple<int, int>(4, 30), new Tuple<int, int>(4, 29),
      new Tuple<int, int>(4, 5), new Tuple<int, int>(4, 19),
      new Tuple<int, int>(0, 30), new Tuple<int, int>(0, 29),
      new Tuple<int, int>(0, 11), new Tuple<int, int>(1, 1),
      new Tuple<int, int>(1, 25), new Tuple<int, int>(1, 11),
      new Tuple<int, int>(2, 1), new Tuple<int, int>(2, 25),
      new Tuple<int, int>(2, 11), new Tuple<int, int>(3, 1),
      new Tuple<int, int>(3, 25), new Tuple<int, int>(3, 11),
      new Tuple<int, int>(4, 1), new Tuple<int, int>(4, 25),
      new Tuple<int, int>(4, 11), new Tuple<int, int>(0, 1),
      new Tuple<int, int>(0, 25), new Tuple<int, int>(0, 13),
      new Tuple<int, int>(1, 27), new Tuple<int, int>(1, 13),
      new Tuple<int, int>(2, 27), new Tuple<int, int>(2, 13),
      new Tuple<int, int>(3, 27), new Tuple<int, int>(3, 13),
      new Tuple<int, int>(4, 27), new Tuple<int, int>(4, 13),
      new Tuple<int, int>(0, 27), new Tuple<int, int>(1, 9),
      new Tuple<int, int>(2, 9), new Tuple<int, int>(3, 9),
      new Tuple<int, int>(4, 9), new Tuple<int, int>(0, 9),
      new Tuple<int, int>(0, 15), new Tuple<int, int>(0, 16),
      new Tuple<int, int>(0, 17), new Tuple<int, int>(0, 18),
      new Tuple<int, int>(1, 37), new Tuple<int, int>(1, 33),
      new Tuple<int, int>(1, 35), new Tuple<int, int>(1, 20),
      new Tuple<int, int>(1, 15), new Tuple<int, int>(1, 16),
      new Tuple<int, int>(1, 17), new Tuple<int, int>(1, 18),
      new Tuple<int, int>(2, 37), new Tuple<int, int>(2, 33),
      new Tuple<int, int>(2, 35), new Tuple<int, int>(2, 20),
      new Tuple<int, int>(2, 15), new Tuple<int, int>(2, 16),
      new Tuple<int, int>(2, 17), new Tuple<int, int>(2, 18),
      new Tuple<int, int>(3, 37), new Tuple<int, int>(3, 33),
      new Tuple<int, int>(3, 35), new Tuple<int, int>(3, 20),
      new Tuple<int, int>(3, 15), new Tuple<int, int>(3, 16),
      new Tuple<int, int>(3, 17), new Tuple<int, int>(3, 18),
      new Tuple<int, int>(4, 37), new Tuple<int, int>(4, 33),
      new Tuple<int, int>(4, 35), new Tuple<int, int>(4, 20),
      new Tuple<int, int>(4, 15), new Tuple<int, int>(4, 16),
      new Tuple<int, int>(4, 17), new Tuple<int, int>(4, 18),
      new Tuple<int, int>(0, 37), new Tuple<int, int>(0, 33),
      new Tuple<int, int>(0, 35), new Tuple<int, int>(0, 20),
      new Tuple<int, int>(0, 24), new Tuple<int, int>(0, 6),
      new Tuple<int, int>(0, 10), new Tuple<int, int>(1, 31),
      new Tuple<int, int>(1, 32), new Tuple<int, int>(1, 34),
      new Tuple<int, int>(1, 0), new Tuple<int, int>(1, 24),
      new Tuple<int, int>(1, 6), new Tuple<int, int>(1, 10),
      new Tuple<int, int>(2, 31), new Tuple<int, int>(2, 32),
      new Tuple<int, int>(2, 34), new Tuple<int, int>(2, 0),
      new Tuple<int, int>(2, 24), new Tuple<int, int>(2, 6),
      new Tuple<int, int>(2, 10), new Tuple<int, int>(3, 31),
      new Tuple<int, int>(3, 32), new Tuple<int, int>(3, 34),
      new Tuple<int, int>(3, 0), new Tuple<int, int>(3, 24),
      new Tuple<int, int>(3, 6), new Tuple<int, int>(3, 10),
      new Tuple<int, int>(4, 31), new Tuple<int, int>(4, 32),
      new Tuple<int, int>(4, 34), new Tuple<int, int>(4, 0),
      new Tuple<int, int>(4, 24), new Tuple<int, int>(4, 6),
      new Tuple<int, int>(4, 10), new Tuple<int, int>(0, 31),
      new Tuple<int, int>(0, 32), new Tuple<int, int>(0, 34),
      new Tuple<int, int>(0, 0), new Tuple<int, int>(0, 7),
      new Tuple<int, int>(0, 12), new Tuple<int, int>(1, 2),
      new Tuple<int, int>(1, 28), new Tuple<int, int>(1, 26),
      new Tuple<int, int>(1, 7), new Tuple<int, int>(1, 12),
      new Tuple<int, int>(2, 2), new Tuple<int, int>(2, 28),
      new Tuple<int, int>(2, 26), new Tuple<int, int>(2, 7),
      new Tuple<int, int>(2, 12), new Tuple<int, int>(3, 2),
      new Tuple<int, int>(3, 28), new Tuple<int, int>(3, 26),
      new Tuple<int, int>(3, 7), new Tuple<int, int>(3, 12),
      new Tuple<int, int>(4, 2), new Tuple<int, int>(4, 28),
      new Tuple<int, int>(4, 26), new Tuple<int, int>(4, 7),
      new Tuple<int, int>(4, 12), new Tuple<int, int>(0, 2),
      new Tuple<int, int>(0, 28), new Tuple<int, int>(0, 26),
      new Tuple<int, int>(0, 14), new Tuple<int, int>(1, 3),
      new Tuple<int, int>(1, 8), new Tuple<int, int>(1, 14),
      new Tuple<int, int>(2, 3), new Tuple<int, int>(2, 8),
      new Tuple<int, int>(2, 14), new Tuple<int, int>(3, 3),
      new Tuple<int, int>(3, 8), new Tuple<int, int>(3, 14),
      new Tuple<int, int>(4, 3), new Tuple<int, int>(4, 8),
      new Tuple<int, int>(4, 14), new Tuple<int, int>(0, 3),
      new Tuple<int, int>(0, 8), new Tuple<int, int>(1, 4),
      new Tuple<int, int>(2, 4), new Tuple<int, int>(3, 4),
      new Tuple<int, int>(4, 4), new Tuple<int, int>(0, 4),
    };

    private OPCAPI opcAPI;
    private readonly Configuration config;
    // Live counters, not config: the OPC send rate is reported here.
    private readonly RuntimeTelemetry telemetry;
    // The tempo service (owned by the Operator, not part of Configuration),
    // read for the beat-synced flash-off in the per-frame color cache.
    private readonly BeatBroadcaster beat;
    private readonly List<Visualizer> visualizers;
    // The subset of `visualizers` that are layerable (implement
    // DomeLayerVisualizer), plus a key→visualizer index. Populated at
    // registration (startup) alongside `visualizers`. The compositor blends
    // these; debug visualizers are not layerable and are absent here.
    private readonly List<DomeLayerVisualizer> layerVisualizers =
      new List<DomeLayerVisualizer>();
    private readonly Dictionary<string, DomeLayerVisualizer>
      layerVisualizersByKey = new Dictionary<string, DomeLayerVisualizer>();
    private static readonly int maxStripLength;

    // The dome is wired as 10 "cables": each of the 5 control boxes drives 8
    // strands split across two parallel ethernet cables, A (strands 0-3) and B
    // (strands 4-7). We identify a cable by box*2 + half (half 0 = A, 1 = B), so
    // cable ids run 0..9. Calibration permutes which controller cable feeds which
    // physical dome endpoint; see GetDeviceIndexes and config.domeCableMapping.
    private const int StrandsPerCable = 4;
    public const int NumCables = 10;
    // Struts carried by cable A (strands 0-3) of every box, and the total per
    // box (38). Computed from controlBoxStrutOrder so the A/B boundary stays in
    // sync if the strand layout ever changes.
    private static readonly int cableAStrutCount;
    private static readonly int domeStrutsPerBox;

    // controllerForEndpoint[e] = the controller cable (box*2 + half) whose data
    // physically reaches dome endpoint e (same box*2 + half labeling under the
    // hard-coded layout). This is the inverse of config.domeCableMapping (which
    // records, per controller cable, the endpoint that lit during calibration).
    // Identity by default, so an uncalibrated dome behaves exactly as before.
    private readonly int[] controllerForEndpoint = new int[NumCables];
    // Buffers handed out by MakeDomeOutputBuffer, tracked so their baked-in
    // device indexes can be recomputed in place when the cable mapping changes,
    // letting a calibration take effect live without restarting the app.
    private readonly List<LEDDomeOutputBuffer> buffers =
      new List<LEDDomeOutputBuffer>();

    private static int calculateMaxStripLength() {
      int maxLength = 0;
      foreach (LEDDomeStrutTypes[] struts in controlBoxStrutOrder) {
        int length = 0;
        foreach (LEDDomeStrutTypes type in struts) {
          length += strutLengths[type];
        }
        if (length > maxLength) {
          maxLength = length;
        }
      }
      return maxLength;
    }

    static LEDDomeOutput() {
      maxStripLength = calculateMaxStripLength();
      int aCount = 0;
      for (int s = 0; s < StrandsPerCable; s++) {
        aCount += controlBoxStrutOrder[s].Length;
      }
      cableAStrutCount = aCount;
      int total = 0;
      foreach (LEDDomeStrutTypes[] strand in controlBoxStrutOrder) {
        total += strand.Length;
      }
      domeStrutsPerBox = total;
    }

    // Live wand angle for the prism blends' "Follow Orientation" option
    // (docs/prism.md). Nullable — a dome wired up without an orientation source
    // simply never follows and the blends use their static angle.
    private readonly OrientationAngleProvider orientationAngle;

    public LEDDomeOutput(
      Configuration config, RuntimeTelemetry telemetry, BeatBroadcaster beat,
      OrientationAngleProvider orientationAngle = null
    ) {
      this.config = config;
      this.telemetry = telemetry;
      this.beat = beat;
      this.orientationAngle = orientationAngle;
      this.visualizers = new List<Visualizer>();
      this.RebuildCableMapping();
      this.config.PropertyChanged += ConfigUpdated;
    }

    // Rebuilds controllerForEndpoint (the inverse of config.domeCableMapping)
    // and re-bakes the device indexes of every outstanding buffer so a new
    // calibration takes effect immediately. Falls back to the identity mapping
    // if the config value is missing or not a valid permutation of 0..9, so a
    // corrupt or short config can never scramble or crash output.
    private void RebuildCableMapping() {
      int[] mapping = this.config.domeCableMapping;
      bool valid = mapping != null && mapping.Length == NumCables;
      if (valid) {
        var seen = new bool[NumCables];
        foreach (int endpoint in mapping) {
          if (endpoint < 0 || endpoint >= NumCables || seen[endpoint]) {
            valid = false;
            break;
          }
          seen[endpoint] = true;
        }
      }
      for (int controller = 0; controller < NumCables; controller++) {
        int endpoint = valid ? mapping[controller] : controller;
        this.controllerForEndpoint[endpoint] = controller;
      }
      lock (this.buffers) {
        foreach (LEDDomeOutputBuffer buffer in this.buffers) {
          this.RebakeBuffer(buffer);
        }
      }
    }

    // Recomputes the cached (control box, pixel-within-box) address of every
    // pixel in a buffer from its strut identity, applying the current cable
    // mapping. Called when the mapping changes; MakeDomeOutputBuffer bakes the
    // same values in once at creation.
    private void RebakeBuffer(LEDDomeOutputBuffer buffer) {
      for (int i = 0; i < buffer.pixels.Length; i++) {
        var deviceIndexes = this.GetDeviceIndexes(
          buffer.pixels[i].strutIndex,
          buffer.pixels[i].strutLEDIndex
        );
        buffer.pixels[i].controlBoxIndex = deviceIndexes.Item1;
        buffer.pixels[i].controlBoxPixelIndex = deviceIndexes.Item2;
      }
    }

    public void RegisterVisualizer(Visualizer visualizer) {
      this.visualizers.Add(visualizer);
      this.visualizersArray = null;
      if (visualizer is DomeLayerVisualizer layerVisualizer) {
        this.layerVisualizers.Add(layerVisualizer);
        this.layerVisualizersByKey[layerVisualizer.LayerKey] = layerVisualizer;
      }
    }

    // Cached snapshot of `visualizers`, rebuilt only when a visualizer is
    // registered (startup) rather than allocated fresh every Operator frame.
    private Visualizer[] visualizersArray;
    public Visualizer[] GetVisualizers() {
      return this.visualizersArray
        ?? (this.visualizersArray = this.visualizers.ToArray());
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == nameof(this.config.domeCableMapping)) {
        this.RebuildCableMapping();
        return;
      }
      if (e.PropertyName == nameof(this.config.domeLayerStack)) {
        // Force the operator thread to re-resolve the stack next Composite.
        this.resolvedStackValid = false;
        return;
      }
      if (
        e.PropertyName != "domeBeagleboneOPCAddress" &&
        e.PropertyName != "domeOutputInSeparateThread" &&
        e.PropertyName != "domeEnabled"
      ) {
        return;
      }
      if (this.opcAPI != null) {
        this.opcAPI.Active = false;
      }
      if (this.active && this.config.domeEnabled) {
        this.initializeOPCAPI();
      }
    }

    private void initializeOPCAPI() {
      var opcAddress = this.config.domeBeagleboneOPCAddress;
      string[] parts = opcAddress.Split(':');
      if (parts.Length < 3) {
        opcAddress += ":0"; // default to channel 0
      }
      this.opcAPI = new OPCAPI(
        opcAddress,
        this.config.domeOutputInSeparateThread,
        newFPS => this.telemetry.DomeBeagleboneOPCFPS = newFPS
      );
      this.opcAPI.Active = this.active;
    }

    private bool active = false;
    public bool Active {
      get {
        return this.active;
      }
      set {
        if (value == this.active) {
          return;
        }
        this.active = value;
        if (value && this.config.domeEnabled) {
          this.initializeOPCAPI();
        } else if (this.opcAPI != null) {
          this.opcAPI.Active = false;
        }
      }
    }

    public bool Enabled {
      get {
        return this.config.domeEnabled || this.config.domeSimulationEnabled;
      }
    }

    public void OperatorUpdate() {
      // Blend this frame's layer stack and push it to the wire/simulator BEFORE
      // opcAPI.OperatorUpdate() sends and before we invalidate the frame color
      // cache. The visualizers ran (into their own buffers) earlier this frame
      // with the cache still valid; Composite reads only their buffers, so the
      // ordering composite -> opc send -> cache invalidate is required. See the
      // GetGradientColor cache note in EnsureFrameColorCache.
      this.Composite();
      if (this.opcAPI != null) {
         this.opcAPI.OperatorUpdate();
      }
      // The operator runs Visualize() on every winning visualizer and then this
      // OperatorUpdate() once per output, so this marks the end of a frame: drop
      // the per-frame color snapshot so the next frame's first pixel recomputes
      // it. See EnsureFrameColorCache.
      this.frameColorCacheValid = false;
    }

    // Scratch composite frame the layer stack blends into, reused every frame.
    // Lazily created via MakeDomeOutputBuffer so it participates in cable-mapping
    // rebakes and is index-aligned pixel-for-pixel with every layer buffer.
    private LEDDomeOutputBuffer compositeBuffer;

    // Wall clock for the global hue rotation's per-frame increment. The
    // rotation is applied to the layers' own persistent buffers (which are
    // faded, not cleared, so in-place rotations accumulate across frames) —
    // never to the composite, whose pixels include this frame's fresh draws.
    private readonly FrameClock hueClock = new FrameClock();

    // Pre-pass snapshot the spatial prism blends (ChromaticFringe, EdgeSpectrum)
    // read while they mutate compositeBuffer — one reusable copy, lazily created
    // like compositeBuffer. Never touched unless such a blend is active.
    private LEDDomeOutputBuffer scratchBuffer;
    // Wall-clock seconds accumulated while compositing, driving the time-varying
    // prism params (ChromaticFringe angle spin, Iridescence light spin). Ticked
    // every frame off its own FrameClock so the motion is smooth and frame-rate
    // independent; unbounded but double-precision holds seconds fine.
    private double prismSeconds = 0;
    private readonly FrameClock prismClock = new FrameClock();

    // One layer of config.domeLayerStack resolved to a live visualizer plus its
    // blend settings, cached so the per-frame path allocates nothing. Whether the
    // visualizer actually ran (Enabled) is still checked per frame, not cached.
    private struct ResolvedLayer {
      public DomeLayerVisualizer visualizer;
      public DomeBlend blend;
      public double opacity;
      // The immutable stack snapshot entry, held so the compositor can read a
      // blend's CompositorConsumed params (the prism family) without re-scanning
      // config.domeLayerStack. Safe to alias: published settings are never
      // mutated in place (whole-stack snapshot swap), and resolvedStack is
      // invalidated whenever domeLayerStack changes.
      public DomeLayerSettings settings;
    }
    // Rebuilt lazily on the operator thread whenever the stack changes. volatile
    // so a set-false from ConfigUpdated (UI/web thread) is seen promptly by the
    // operator thread; the resolved array is only ever read/written on the
    // operator thread.
    private volatile bool resolvedStackValid = false;
    private ResolvedLayer[] resolvedStack = System.Array.Empty<ResolvedLayer>();

    private void EnsureResolvedStack() {
      if (this.resolvedStackValid) {
        return;
      }
      var resolved = new List<ResolvedLayer>();
      List<DomeLayerSettings> stack = this.config.domeLayerStack;
      if (stack != null) {
        foreach (DomeLayerSettings layer in stack) {
          if (
            layer == null || !layer.Enabled || layer.VisualizerKey == null
          ) {
            continue;
          }
          if (!this.layerVisualizersByKey.TryGetValue(
            layer.VisualizerKey, out DomeLayerVisualizer visualizer
          )) {
            continue;
          }
          resolved.Add(new ResolvedLayer {
            visualizer = visualizer,
            // A name the registry doesn't know (a hand-edited config file —
            // the validator rejects unknowns on every publish path) falls
            // back to Over, like the retired enum switch's default arm.
            blend = DomeBlend.FromName(layer.BlendMode) ?? DomeBlend.Over,
            opacity = layer.Opacity,
            settings = layer,
          });
        }
      }
      this.resolvedStack = resolved.ToArray();
      this.resolvedStackValid = true;
    }

    // Blends the active layer stack into compositeBuffer and pushes exactly one
    // frame (one WriteBuffer + one Flush) to the wire and simulator. No-ops when
    // no layer ran this frame: either a debug visualizer owns the frame via its
    // own SetPixel + self-Flush path (which passes through untouched), or nothing
    // is eligible at all and the dome holds its last frame.
    private void Composite() {
      this.EnsureResolvedStack();
      ResolvedLayer[] stack = this.resolvedStack;

      // Advance the prism spin clock every frame (even when no prism blend is
      // active) so the time-varying params move in real wall-clock time and
      // re-enabling a spin doesn't jump. Tick() returns nominal frames; convert
      // to seconds.
      this.prismSeconds += this.prismClock.Tick() / FrameClock.NominalFps;

      // A layer contributes only if the Operator scheduled its visualizer this
      // frame (Enabled true: inputs enabled, not quarantined). The bottom-most
      // such layer seeds the composite; the rest blend over it in stack order.
      bool destInitialized = false;
      for (int s = 0; s < stack.Length; s++) {
        ResolvedLayer layer = stack[s];
        if (!layer.visualizer.Enabled) {
          continue;
        }
        LEDDomeOutputBuffer src = layer.visualizer.LayerBuffer;
        if (this.compositeBuffer == null) {
          this.compositeBuffer = this.MakeDomeOutputBuffer();
        }
        if (!destInitialized) {
          this.compositeBuffer.CompositeBottom(src, layer.opacity);
          destInitialized = true;
        } else {
          this.BlendLayer(layer, src);
        }
      }
      if (!destInitialized) {
        return;
      }
      this.WriteBuffer(this.compositeBuffer);
      this.Flush();
      // Post-frame: advance the global hue rotation on the layers' persisted
      // pixels, so it lands "before" (underneath) whatever the layers draw
      // next frame — fresh pixels always reach the wire unrotated.
      this.ApplyGlobalHueRotation();
    }

    // Blend one upper layer into compositeBuffer: take the pre-pass snapshot if
    // the blend samples neighbors of the buffer it mutates, then hand the blend
    // everything it needs (buffers, the layer's settings for its Params, and
    // the prism clock / wand angle source for the time-varying params).
    private void BlendLayer(ResolvedLayer layer, LEDDomeOutputBuffer src) {
      DomeBlend blend = layer.blend;
      if (blend.NeedsSnapshot) {
        this.SnapshotComposite();
      }
      blend.Blend(new DomeBlendContext(
        this.compositeBuffer, src,
        blend.NeedsSnapshot ? this.scratchBuffer : null,
        layer.settings, layer.opacity, this.prismSeconds,
        this.orientationAngle));
    }

    // Copy the current composite into the reusable scratch buffer so a spatial
    // prism blend can read the pre-pass state while it mutates compositeBuffer.
    // Taken immediately before each spatial blend because the composite changes
    // between layers. The scratch buffer is lazily created via
    // MakeDomeOutputBuffer so it stays index-aligned with the composite.
    private void SnapshotComposite() {
      if (this.scratchBuffer == null) {
        this.scratchBuffer = this.MakeDomeOutputBuffer();
      }
      this.scratchBuffer.CopyFrom(this.compositeBuffer);
    }

    // The output-wide "Hue Rotation" (the knob that used to be applied
    // per-layer, redundantly and inconsistently, by Paintbrush and Radial).
    // Driven by domeGlobalHueSpeed: 0 = off; otherwise higher = slower. The
    // beat pulse (3p^2 - 3p + 1, always positive so the rotation only moves
    // forward) reproduces the modulation the Paintbrush layer applied on its
    // own buffer.
    //
    // Sequencing: global postprocessing must only ever touch *already
    // existing* pixels — a pixel a layer painted this frame reaches the wire
    // at exactly its drawn hue, and starts rotating from the next frame on
    // (same contract as the per-layer Fade each visualizer applies before it
    // draws). So rather than rotating the composite (which includes this
    // frame's fresh draws), rotate each contributing layer's persistent
    // buffer by one frame's increment *after* the frame has been composited
    // and written. The layer buffers are faded, not cleared, so the per-frame
    // increments accumulate naturally — older trail pixels have rotated
    // further than fresh ones, restoring the along-trail hue gradient.
    private void ApplyGlobalHueRotation() {
      // Tick every frame (even when off) so re-enabling doesn't jump.
      double frameScale = this.hueClock.Tick();
      if (this.config.domeGlobalHueSpeed <= 0) {
        return;
      }
      double rate = Math.Pow(10, -this.config.domeGlobalHueSpeed);
      double p = this.beat.ProgressThroughMeasure;
      double mod = 3 * p * p - 3 * p + 1;
      double delta = rate * mod * frameScale;
      ResolvedLayer[] stack = this.resolvedStack;
      for (int s = 0; s < stack.Length; s++) {
        DomeLayerVisualizer visualizer = stack[s].visualizer;
        // Rotate exactly the buffers that contributed to this frame, once
        // each — two stack entries with the same VisualizerKey share one
        // visualizer instance (and buffer), so skip repeats.
        if (!visualizer.Enabled) {
          continue;
        }
        bool alreadyRotated = false;
        for (int t = 0; t < s; t++) {
          if (ReferenceEquals(stack[t].visualizer, visualizer)) {
            alreadyRotated = true;
            break;
          }
        }
        if (alreadyRotated) {
          continue;
        }
        visualizer.LayerBuffer.HueRotate(delta);
      }
    }

    // Per-frame snapshot of the beat/brightness state that every GetSingleColor /
    // GetGradientColor call would otherwise recompute for each of the ~4500 dome
    // pixels. CurrentlyFlashedOff takes a lock and reads the wall clock, so
    // querying it (and the domeMaxBrightness*domeBrightness product) once per
    // frame instead of once per pixel removes millions of lock acquisitions and
    // DateTime.Now reads per second from the operator thread. These fields are
    // only ever touched on the operator thread (visualizers and OperatorUpdate
    // both run there), so they need no synchronization.
    private bool frameColorCacheValid = false;
    private bool frameFlashedOff;
    private double frameBrightness;

    private void EnsureFrameColorCache() {
      if (this.frameColorCacheValid) {
        return;
      }
      this.frameFlashedOff = this.beat.CurrentlyFlashedOff;
      this.frameBrightness =
        this.config.domeMaxBrightness * this.config.domeBrightness;
      this.frameColorCacheValid = true;
    }

    // Frames for the on-screen dome simulator. The queue lives here, with its
    // producer (split off Configuration — arch_issues item 5); the single
    // consumer is DomeSimulatorWindow, which reaches it via Operator.DomeOutput.
    public ConcurrentQueue<DomeLEDCommand> SimulatorCommandQueue { get; } =
      new ConcurrentQueue<DomeLEDCommand>();

    // True while the simulator window is open and actually draining
    // SimulatorCommandQueue. A plain bool read/write is atomic, which is all
    // the operator thread needs to gate its enqueues on a live consumer.
    public bool SimulatorHasConsumer { get; set; }

    // Only feed SimulatorCommandQueue when simulation is on AND something is
    // actually draining the queue. Without the consumer check, setting
    // domeSimulationEnabled with no simulator window open (e.g. flipped on over
    // the web) would grow the queue without bound — a fresh int[] frame
    // snapshot up to 400x/sec with nobody dequeuing it.
    private bool ShouldEnqueueDomeCommand =>
      this.config.domeSimulationEnabled && this.SimulatorHasConsumer;

    // Hard ceiling on SimulatorCommandQueue depth. The consumer
    // (DomeSimulatorWindow) drains on a ~100Hz UI tick, but the operator thread
    // produces at up to 400Hz, and if the UI thread stalls (window dragged,
    // minimized, slow machine) the queue would otherwise grow without bound. A
    // display only needs the most recent state, so past this cap we drop the
    // oldest commands. Sized well above a single full-dome frame: the per-pixel
    // diagnostic/calibration visualizers enqueue one command per lit pixel
    // (~4,500) plus a Flush every frame, and several such frames can sit in the
    // queue between UI ticks even when the consumer is keeping up — so trimming
    // engages only under a genuine stall, never during healthy rendering.
    private const int SimulatorCommandQueueCap = 20000;

    // Enqueue a simulator command, dropping the oldest queued commands if the
    // backlog has grown past SimulatorCommandQueueCap. TryDequeue here races the
    // consumer's own dequeues, which ConcurrentQueue handles; the consumer
    // tolerates a command being pulled out from under it (see
    // DomeSimulatorWindow.Update).
    private void EnqueueSimulatorCommand(DomeLEDCommand command) {
      this.SimulatorCommandQueue.Enqueue(command);
      while (
        this.SimulatorCommandQueue.Count > SimulatorCommandQueueCap &&
        this.SimulatorCommandQueue.TryDequeue(out _)
      ) {
      }
    }

    public void Flush() {
      if (this.opcAPI != null) {
         this.opcAPI.Flush();
      }
      if (this.ShouldEnqueueDomeCommand) {
        this.EnqueueSimulatorCommand(
          new DomeLEDCommand() { isFlush = true }
        );
      }
    }

    private void SetDevicePixel(int controlBoxIndex, int pixelIndex, int color) {
      lock (this.visualizers) {
        if (this.opcAPI != null) {
          int totalPixelIndex = controlBoxIndex * (maxStripLength * 8) + pixelIndex;
          this.opcAPI.SetPixel(totalPixelIndex, color);
        }
      }
    }

    // Raw (identity) device address: which control box and pixel-within-box a
    // strut's LED occupies under the hard-coded strutPositions wiring, ignoring
    // any calibrated cable permutation. This is the canonical "what the program
    // believes it is lighting" used by the dome-mapping calibration.
    private Tuple<int, int> GetDeviceIndexesRaw(int strutIndex, int ledIndex) {
      int pixelIndex = ledIndex;
      Tuple<int, int> strutPosition = strutPositions[strutIndex];
      int strutsLeft = strutPosition.Item2;
      int i = 0;
      while (controlBoxStrutOrder[i].Length <= strutsLeft) {
        strutsLeft -= controlBoxStrutOrder[i].Length;
        i++;
        pixelIndex += maxStripLength;
      }
      for (int j = 0; j < strutsLeft; j++) {
        pixelIndex += strutLengths[controlBoxStrutOrder[i][j]];
      }
      return Tuple.Create(strutPosition.Item1, pixelIndex);
    }

    // Mapped device address: the raw address re-routed through the calibrated
    // cable permutation. Each LED lives on a physical endpoint (box*2 + half);
    // controllerForEndpoint tells us which controller cable actually feeds that
    // endpoint, so we relocate the LED onto that cable, preserving its
    // strand-within-cable and offset-within-strand (every cable is the same
    // 4-strand x maxStripLength shape in the OPC stream, so only box/half
    // change). Identity mapping reproduces GetDeviceIndexesRaw exactly.
    private Tuple<int, int> GetDeviceIndexes(int strutIndex, int ledIndex) {
      Tuple<int, int> raw = this.GetDeviceIndexesRaw(strutIndex, ledIndex);
      int box = raw.Item1;
      int strandSlot = raw.Item2 / maxStripLength;
      int offsetWithinStrand = raw.Item2 - strandSlot * maxStripLength;
      int half = strandSlot < StrandsPerCable ? 0 : 1;
      int strandWithinCable = strandSlot - half * StrandsPerCable;
      int endpoint = box * 2 + half;
      int controller = this.controllerForEndpoint[endpoint];
      int newBox = controller / 2;
      int newHalf = controller % 2;
      int newStrandSlot = newHalf * StrandsPerCable + strandWithinCable;
      return Tuple.Create(
        newBox,
        newStrandSlot * maxStripLength + offsetWithinStrand
      );
    }

    public void SetPixel(int strutIndex, int ledIndex, int color) {
      Tuple<int, int> deviceIndexes = this.GetDeviceIndexes(strutIndex, ledIndex);
      this.SetDevicePixel(deviceIndexes.Item1, deviceIndexes.Item2, color);

      if (this.ShouldEnqueueDomeCommand) {
        this.EnqueueSimulatorCommand(new DomeLEDCommand() {
          strutIndex = strutIndex,
          ledIndex = ledIndex,
          color = color,
        });
      }
    }

    // Like SetPixel but writes to the raw (unpermuted) control-box address, so
    // the dome-mapping calibration can light exactly one physical controller
    // cable regardless of the current (possibly wrong or identity) calibration.
    public void SetPixelRaw(int strutIndex, int ledIndex, int color) {
      Tuple<int, int> deviceIndexes =
        this.GetDeviceIndexesRaw(strutIndex, ledIndex);
      this.SetDevicePixel(deviceIndexes.Item1, deviceIndexes.Item2, color);

      if (this.ShouldEnqueueDomeCommand) {
        this.EnqueueSimulatorCommand(new DomeLEDCommand() {
          strutIndex = strutIndex,
          ledIndex = ledIndex,
          color = color,
        });
      }
    }

    public LEDDomeOutputBuffer MakeDomeOutputBuffer() {
      List<LEDDomeOutputPixel> pixels = new List<LEDDomeOutputPixel>();

      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        var leds = LEDDomeOutput.GetNumLEDs(i);
        for (int j = 0; j < leds; j++) {
          var point = StrutLayoutFactory.GetProjectedLEDPoint(i, j);
          var deviceIndexes = GetDeviceIndexes(i, j);
          LEDDomeOutputPixel pixel = new LEDDomeOutputPixel();
          pixel.strutIndex = i;
          pixel.strutLEDIndex = j;
          pixel.controlBoxIndex = deviceIndexes.Item1;
          pixel.controlBoxPixelIndex = deviceIndexes.Item2;
          pixel.x = point.Item1;
          pixel.y = point.Item2;
          // Leave color/coverage at their struct defaults (0 => transparent
          // black). Going through the `color` setter here would mark every pixel
          // opaque (_a = 1), which would make a foreground Over layer paint solid
          // black everywhere it hadn't drawn yet.

          pixels.Add(pixel);
        }
      }

      var buffer = new LEDDomeOutputBuffer(pixels.ToArray());
      // Track the buffer so its baked-in device indexes can be re-derived in
      // place if the cable mapping changes (see RebuildCableMapping).
      lock (this.buffers) {
        this.buffers.Add(buffer);
      }
      return buffer;
    }

    // The strut indices physically carried by one controller cable
    // (boxIndex, half; half 0 = ethernet A = strands 0-3, 1 = B = strands 4-7),
    // under the raw hard-coded wiring. Used by the dome-mapping calibration both
    // to light a single cable and to draw the clickable per-endpoint regions.
    public static List<int> GetControllerCableStruts(int boxIndex, int half) {
      int start = half == 0 ? 0 : cableAStrutCount;
      int end = half == 0 ? cableAStrutCount : domeStrutsPerBox;
      var struts = new List<int>();
      for (int localIndex = start; localIndex < end; localIndex++) {
        int strutIndex = FindStrutIndex(boxIndex, localIndex);
        if (strutIndex != -1) {
          struts.Add(strutIndex);
        }
      }
      return struts;
    }

    public void WriteBuffer(LEDDomeOutputBuffer buffer) {
      // Snapshot opcAPI once instead of null-checking + locking per pixel (P4).
      // The visualizers list is only mutated at registration time, so the
      // per-pixel lock in SetDevicePixel guarded nothing here.
      OPCAPI opcAPI = this.opcAPI;
      if (opcAPI == null) {
        return;
      }
      // Use the device indexes precomputed once in MakeDomeOutputBuffer rather
      // than re-deriving the control-box/pixel mapping for every pixel every
      // frame (P3).
      int stride = maxStripLength * 8;
      bool simulationEnabled = this.ShouldEnqueueDomeCommand;
      // When simulating, snapshot the whole frame's colors and hand the
      // simulator a single command, instead of enqueueing one command per pixel
      // (~4000 interlocked ConcurrentQueue ops + an equal number of dequeues and
      // per-command geometry recomputes every frame). A fresh array per frame is
      // required because the buffer is mutated in place next frame and the queue
      // may hold several frames at once, so the snapshot can't be reused.
      int[] frame = simulationEnabled ? new int[buffer.pixels.Length] : null;
      for (int i = 0; i < buffer.pixels.Length; i++) {
        LEDDomeOutputPixel pixel = buffer.pixels[i];
        int totalPixelIndex =
          pixel.controlBoxIndex * stride + pixel.controlBoxPixelIndex;
        opcAPI.SetPixel(totalPixelIndex, pixel.color);
        if (simulationEnabled) {
          frame[i] = pixel.color;
        }
      }
      if (simulationEnabled) {
        this.EnqueueSimulatorCommand(new DomeLEDCommand() {
          frame = frame,
        });
      }
    }

    /**
     * This function takes as input a controlBoxIndex, and a special index
     * called controlBoxStrutIndex. This second index goes from 0-37, and it
     * identifies a strut uniquely for a given control box. (There are currently
     * 190 struts and 5 control boxes). The order is such so that the struts
     * appear in the order they are plugged into the control box, eg. all of the
     * struts plugged into the first of the eight outputs on the control box,
     * and then all of the struts plugged into the second, etc. This method is
     * primarily useful for debugging.
     */
    public static int FindStrutIndex(
      int controlBoxIndex,
      int controlBoxStrutIndex
    ) {
      for (int i = 0; i < strutPositions.Length; i++) {
        var strutPosition = strutPositions[i];
        if (
          controlBoxIndex == strutPosition.Item1 &&
          controlBoxStrutIndex == strutPosition.Item2
        ) {
          return i;
        }
      }
      return -1;
    }

    public static int GetNumStruts() {
      return strutPositions.Length;
    }

    public static int GetNumLEDs(int strutIndex) {
      var strutPosition = strutPositions[strutIndex];
      int strutsLeft = strutPosition.Item2;
      int i = 0;
      while (controlBoxStrutOrder[i].Length <= strutsLeft) {
        strutsLeft -= controlBoxStrutOrder[i].Length;
        i++;
      }
      return strutLengths[controlBoxStrutOrder[i][strutsLeft]];
    }

    public int GetSingleColor(int index) {
      this.EnsureFrameColorCache();
      if (this.frameFlashedOff) {
        return 0x000000;
      }
      // Bank 0 is hard-wired: the 8-bank palette switcher (colorPaletteIndex)
      // was retired (docs/HUD_overhaul.md). Visualizers pass palette-relative
      // indices 0-7, which now read slots 0-7 directly.
      int absoluteIndex = index;
      return LEDColor.ScaleColor(
        this.config.colorPalette.GetSingleColor(absoluteIndex),
        this.frameBrightness
      );
    }

    public int GetGradientColor(
      int index,
      double pixelPos,
      double focusPos,
      bool wrap
    ) {
      this.EnsureFrameColorCache();
      if (this.frameFlashedOff) {
        return 0x000000;
      }
      // Bank 0 is hard-wired; see GetSingleColor (colorPaletteIndex retired).
      int absoluteIndex = index;
      if (
        this.config.colorPalette.colors == null ||
        this.config.colorPalette.colors.Length <= absoluteIndex ||
        this.config.colorPalette.colors[absoluteIndex] == null
      ) {
        return 0x000000;
      }
      if (!this.config.colorPalette.colors[absoluteIndex].IsGradient) {
        // absoluteIndex is already offset; call the palette directly rather
        // than this.GetSingleColor, which would apply the palette offset again.
        return LEDColor.ScaleColor(
          this.config.colorPalette.GetSingleColor(absoluteIndex),
          this.frameBrightness
        );
      }
      return LEDColor.ScaleColor(
        this.config.colorPalette.GetGradientColor(
          absoluteIndex,
          pixelPos,
          focusPos,
          wrap
        ),
        this.frameBrightness
      );
    }

    public int GetGradientBetweenColors(
      int minIndex,
      int maxIndex,
      double pixelPos,
      double focusPos,
      bool wrap
    ) {
      // Return a color evenly scaled between min index and max index, based on the pixel position.
      if (pixelPos < 0 || pixelPos > 1) {
        throw new ArgumentException("Pixel Position out of range: " + pixelPos.ToString());
      }
      this.EnsureFrameColorCache();
      if (this.frameFlashedOff) {
        return 0x000000;
      }
      var num_colors = maxIndex - minIndex;
      // Which adjacent pair of palette slots pixelPos lands between, as an
      // offset from minIndex. Clamp so pixelPos == 1.0 maps to the last pair
      // (maxIndex-1, maxIndex) instead of overrunning past maxIndex.
      int segment = (int)(pixelPos * num_colors);
      if (segment >= num_colors) {
        segment = num_colors - 1;
      }
      // Offset by minIndex so "gradient between colors 59-63" actually reads
      // slots 59-63 rather than 0-4 of the current palette.
      int minColorIdx = minIndex + segment;
      int maxColorIdx = minColorIdx + 1;
      // Rescale the position so it runs 0..1 between the two chosen slots,
      // clamping to guard against floating-point rounding landing just outside.
      double scaledPixelPos = pixelPos * num_colors - segment;
      if (scaledPixelPos < 0) {
        scaledPixelPos = 0;
      } else if (scaledPixelPos > 1) {
        scaledPixelPos = 1;
      }
      // Bank 0 is hard-wired; see GetSingleColor (colorPaletteIndex retired).
      int absoluteIndexMin = minColorIdx;
      int absoluteIndexMax = maxColorIdx;
      if (
        this.config.colorPalette.colors == null ||
        this.config.colorPalette.colors.Length <= absoluteIndexMin ||
        this.config.colorPalette.colors[absoluteIndexMin] == null
      ) {
        return 0x000000;
      }
      if (
        this.config.colorPalette.colors == null ||
        this.config.colorPalette.colors.Length <= absoluteIndexMax ||
        this.config.colorPalette.colors[absoluteIndexMax] == null
      ) {
        return 0x000000;
      }
      if (!this.config.colorPalette.colors[absoluteIndexMin].IsGradient) {
        // minColorIdx is palette-relative; GetSingleColor re-applies the palette
        // offset itself, so pass it the relative index (not absoluteIndexMin).
        return this.GetSingleColor(minColorIdx);
      }
      // Blend Color1 of the two adjacent slots. Read the palette directly
      // (unscaled) and apply frameBrightness exactly once at the end — routing
      // the endpoints through this.GetSingleColor would pre-scale each by
      // frameBrightness, making the result quadratic in the brightness slider.
      LEDColor color = new LEDColor(
        this.config.colorPalette.GetSingleColor(absoluteIndexMin),
        this.config.colorPalette.GetSingleColor(absoluteIndexMax));
      return LEDColor.ScaleColor(
        color.GradientColor(scaledPixelPos, focusPos, wrap),
        this.frameBrightness
      );
    }
  }

}

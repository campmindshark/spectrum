using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Spectrum.Base;

namespace Spectrum.LEDs {

  enum LEDDomeStrutTypes { Yellow, Red, Blue, Green, Purple, Orange };

  public class LEDDomeOutput : Output, DomeRenderContext {

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

    private readonly Configuration config;
    private readonly IRuntimeSettingsConfiguration runtimeSettings;
    private readonly DomeOpcTransport transport;
    private readonly DomeSimulatorPublisher simulatorPublisher =
      new DomeSimulatorPublisher();
    private readonly DomePaletteSampler paletteSampler;

    internal WaitHandle? PendingOpcConnectWaitHandle =>
      this.transport.PendingConnectWaitHandle;
    // The tempo service (owned by the Operator, not part of Configuration),
    // shared by palette sampling and global hue postprocessing.
    private readonly BeatBroadcaster beat;
    private readonly List<Visualizer> visualizers;
    private readonly DomeCompositor compositor;
    private DomeRenderGeneration renderGeneration;
    // Operator-thread-only frame capture. Visualizers, compositor palette
    // effects, and global hue all read through this same accepted generation.
    private DomeRenderGeneration? frameRenderGeneration;
    private DomeRuntimeFrameSnapshot frameRuntimeSettings =
      DomeRuntimeFrameSnapshot.Empty;
    private DomeOutputSettingsSnapshot frameOutputSettings =
      DomeOutputSettingsSnapshot.Empty;
    private bool operatorFrameActive;
    private long appliedMappingGeneration;
    private long appliedTransportGeneration;
    internal long AppliedMappingGeneration =>
      Volatile.Read(ref this.appliedMappingGeneration);
    internal long AppliedTransportGeneration =>
      Volatile.Read(ref this.appliedTransportGeneration);
    internal event Action? OutputSettingsApplied;
    private int outputSettingsPending;
    private static readonly int maxStripLength;
    private readonly DomeOutputMapper outputMapper;

    // The dome is wired as 10 "cables": each of the 5 control boxes drives 8
    // strands split across two parallel ethernet cables, A (strands 0-3) and B
    // (strands 4-7). We identify a cable by box*2 + half (half 0 = A, 1 = B), so
    // cable ids run 0..9. Calibration permutes which controller cable feeds which
    // physical dome endpoint; see DomeOutputMapper and domeCableMapping.
    private const int StrandsPerCable = DomeOutputMapper.StrandsPerCable;
    public const int NumCables = DomeOutputMapper.NumCables;
    public const int NumDomeBoxes = DomeOutputMapper.NumDomeBoxes;
    public const int NumPortsPerBox = DomeOutputMapper.NumPortsPerBox;
    // Struts carried by cable A (strands 0-3) of every box, and the total per
    // box (38). Computed from controlBoxStrutOrder so the A/B boundary stays in
    // sync if the strand layout ever changes.
    private static readonly int cableAStrutCount;
    private static readonly int domeStrutsPerBox;

    private DomeTopology? topology;

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

    // Live wand angle for the prism blends' "Follow Orientation" option.
    // Nullable — a dome wired up without an orientation source
    // simply never follows and the blends use their static angle.
    public LEDDomeOutput(
      Configuration config, RuntimeTelemetry telemetry, BeatBroadcaster beat,
      OrientationAngleProvider? orientationAngle = null
    ) : this(config, telemetry, beat, orientationAngle, null) {
    }

    private LEDDomeOutput(
      Configuration config,
      RuntimeTelemetry telemetry,
      BeatBroadcaster beat,
      OrientationAngleProvider? orientationAngle,
      TimeSpan? opcMinSendInterval
    ) {
      this.config = config;
      this.runtimeSettings = config as IRuntimeSettingsConfiguration ??
        throw new ArgumentException(
          "LEDDomeOutput requires immutable runtime settings.",
          nameof(config));
      this.outputMapper = new DomeOutputMapper(
        maxStripLength,
        GetNumStruts,
        GetNumLEDs,
        this.GetDeviceIndexesRaw);
      this.transport = new DomeOpcTransport(telemetry, opcMinSendInterval);
      this.beat = beat;
      this.visualizers = new List<Visualizer>();
      DomeShowStateSnapshot initialShowState =
        (config as IDomeShowStateConfiguration)?.DomeShowStateSnapshot ??
          DomeShowStateSnapshot.Empty;
      this.renderGeneration = new DomeRenderGeneration(
        RenderPlan.Empty, initialShowState);
      this.paletteSampler = new DomePaletteSampler(
        beat,
        () => Volatile.Read(ref this.renderGeneration) ??
          DomeRenderGeneration.Empty,
        () => this.runtimeSettings.DomeRuntimeFrameSnapshot);
      this.compositor = new DomeCompositor(
        this.MakeDomeFrame, orientationAngle,
        paletteColor: (palette, position) =>
          this.paletteSampler.GetGradientBetweenColors(
          0, 7, position, 0, false, palette));
      this.frameRenderGeneration = this.renderGeneration;
      this.frameRuntimeSettings =
        this.runtimeSettings.DomeRuntimeFrameSnapshot;
      this.frameOutputSettings =
        this.runtimeSettings.DomeOutputSettingsSnapshot;
      this.outputMapper.Apply(this.frameOutputSettings);
      this.appliedMappingGeneration =
        this.frameOutputSettings.MappingGeneration;
      this.appliedTransportGeneration =
        this.frameOutputSettings.TransportGeneration;
      this.config.PropertyChanged += ConfigUpdated;
    }

    internal LEDDomeOutput(
      Configuration config, RuntimeTelemetry telemetry, BeatBroadcaster beat,
      TimeSpan opcMinSendInterval
    ) : this(config, telemetry, beat, null, opcMinSendInterval) {
      if (opcMinSendInterval < TimeSpan.Zero) {
        throw new ArgumentOutOfRangeException(nameof(opcMinSendInterval));
      }
    }

    public static bool IsValidPortMapping(int[]? mapping) =>
      DomeOutputMapper.IsValidPortMapping(mapping);

    public void RegisterVisualizer(Visualizer visualizer) {
      this.visualizers.Add(visualizer);
      this.visualizersArray = null;
    }

    public void UnregisterVisualizer(Visualizer visualizer) {
      if (this.visualizers.Remove(visualizer)) {
        this.visualizersArray = null;
      }
    }

    // Cached snapshot of `visualizers`, rebuilt only when a visualizer is
    // registered (startup) rather than allocated fresh every Operator frame.
    private Visualizer[]? visualizersArray;
    public Visualizer[] GetVisualizers() {
      return this.visualizersArray
        ?? (this.visualizersArray = this.visualizers.ToArray());
    }

    private void ConfigUpdated(object? sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == nameof(this.config.domeCableMapping) ||
          e.PropertyName == nameof(this.config.domePortMappings) ||
          e.PropertyName == nameof(this.config.domeBeagleboneOPCAddress) ||
          e.PropertyName == nameof(this.config.domeOutputInSeparateThread) ||
          e.PropertyName == nameof(this.config.domeEnabled)) {
        Interlocked.Exchange(ref this.outputSettingsPending, 1);
      }
    }

    // Configuration notifications only mark work. The output/operator caller
    // that is about to address pixels or flush the transport owns the actual
    // reconciliation, so OPC and mapping state are never rewritten from the
    // WPF/Kestrel state-owner thread while a frame is using them.
    private void EnsureOutputSettingsReconciled() {
      if (Interlocked.Exchange(ref this.outputSettingsPending, 0) == 0) {
        return;
      }
      bool changed = false;
      DomeOutputSettingsSnapshot settings =
        this.runtimeSettings.DomeOutputSettingsSnapshot;
      if (settings.MappingGeneration != this.appliedMappingGeneration) {
        this.outputMapper.Apply(settings);
        this.appliedMappingGeneration = settings.MappingGeneration;
        changed = true;
      }
      if (settings.TransportGeneration != this.appliedTransportGeneration) {
        this.appliedTransportGeneration = settings.TransportGeneration;
        changed = true;
        this.transport.ApplySettings(settings);
      }
      if (changed) {
        this.PublishOutputSettingsApplied();
      }
    }

    private void PublishOutputSettingsApplied() {
      try {
        this.OutputSettingsApplied?.Invoke();
      } catch (Exception error) {
        Debug.WriteLine(
          "LEDDomeOutput settings observer failed: " + error);
      }
    }

    public bool Active {
      get { return this.transport.Active; }
      set {
        if (value == this.transport.Active) {
          return;
        }
        if (value) {
          Interlocked.Exchange(ref this.outputSettingsPending, 1);
          this.EnsureOutputSettingsReconciled();
          this.transport.Activate(
            this.runtimeSettings.DomeOutputSettingsSnapshot);
        } else {
          this.transport.Deactivate();
        }
      }
    }

    public bool Enabled {
      get {
        DomeOutputSettingsSnapshot settings =
          this.runtimeSettings.DomeOutputSettingsSnapshot;
        return settings.Enabled || settings.SimulationEnabled ||
          this.WebSimulatorHasConsumer;
      }
    }

    public void OperatorUpdate() {
      this.EnsureOutputSettingsReconciled();
      // Blend this frame's layer stack and push it to the wire/simulator BEFORE
      // opcAPI.OperatorUpdate() sends and before the palette sampler releases
      // its captured frame. The visualizers ran (into their own buffers)
      // earlier this frame with the capture still valid; Composite reads only
      // their buffers, so the ordering composite -> opc send -> release is
      // required.
      DomeRenderGeneration frameGeneration =
        this.frameRenderGeneration ?? Volatile.Read(ref this.renderGeneration);
      DomeFrame? completed = this.compositor.Compose(frameGeneration.Plan);
      if (completed != null) {
        this.WriteBuffer(completed);
        this.Flush();
        this.ApplyGlobalHueRotation(frameGeneration);
      }
      this.transport.OperatorUpdate();
      this.paletteSampler.EndFrame();
      this.frameRenderGeneration = null;
      this.operatorFrameActive = false;
    }

    // The Operator accepts the compiled plan and every persisted value it can
    // consume with one reference write. A failed candidate leaves the complete
    // previous generation live.
    public void PublishRenderGeneration(DomeRenderGeneration generation) {
      if (generation == null) {
        throw new ArgumentNullException(nameof(generation));
      }
      Volatile.Write(ref this.renderGeneration, generation);
      // Preserve DomeCompositor's standalone Plan/Compose API for diagnostics
      // and tests; production composition receives the captured frame plan.
      this.compositor.Publish(generation.Plan);
    }

    // Updates only the immutable show-state half while retaining the accepted
    // plan. Compare/exchange prevents a concurrent plan reconciliation from
    // being overwritten by a palette/global-only publication.
    public void PublishShowState(DomeShowStateSnapshot showState) {
      if (showState == null) {
        throw new ArgumentNullException(nameof(showState));
      }
      while (true) {
        DomeRenderGeneration current =
          Volatile.Read(ref this.renderGeneration) ??
            DomeRenderGeneration.Empty;
        var updated = new DomeRenderGeneration(current.Plan, showState);
        if (ReferenceEquals(
            Interlocked.CompareExchange(
              ref this.renderGeneration, updated, current), current)) {
          return;
        }
      }
    }

    public DomeShowStateSnapshot BeginOperatorFrame(
      DomeRuntimeFrameSnapshot? runtimeSettings = null
    ) {
      DomeRenderGeneration generation =
        Volatile.Read(ref this.renderGeneration) ?? DomeRenderGeneration.Empty;
      this.frameRenderGeneration = generation;
      this.frameRuntimeSettings = runtimeSettings ??
        this.runtimeSettings.DomeRuntimeFrameSnapshot;
      this.frameOutputSettings =
        this.runtimeSettings.DomeOutputSettingsSnapshot;
      this.operatorFrameActive = true;
      this.paletteSampler.BeginFrame(generation, this.frameRuntimeSettings);
      return generation.ShowState;
    }

    // Compatibility helper for direct compositor/output tests.
    public void PublishRenderPlan(RenderPlan plan) {
      DomeRenderGeneration current =
        Volatile.Read(ref this.renderGeneration) ?? DomeRenderGeneration.Empty;
      this.PublishRenderGeneration(new DomeRenderGeneration(
        plan ?? RenderPlan.Empty, current.ShowState));
    }

    public RenderPlan RenderPlan =>
      (Volatile.Read(ref this.renderGeneration) ??
        DomeRenderGeneration.Empty).Plan;

    public DomeShowStateSnapshot ShowState =>
      (Volatile.Read(ref this.renderGeneration) ??
        DomeRenderGeneration.Empty).ShowState;

    // Diagnostics execute on the operator thread and share this exact capture
    // with normal layer renderers and output brightness calculation.
    public DomeRuntimeFrameSnapshot RuntimeFrameSettings =>
      this.operatorFrameActive
        ? this.frameRuntimeSettings
        : this.runtimeSettings.DomeRuntimeFrameSnapshot;
    public DomeOutputSettingsSnapshot OutputSettings =>
      this.operatorFrameActive
        ? this.frameOutputSettings
        : this.runtimeSettings.DomeOutputSettingsSnapshot;

    // Wall clock for the global hue rotation's per-frame increment. The
    // rotation is applied to the layers' own persistent buffers (which are
    // faded, not cleared, so in-place rotations accumulate across frames) —
    // never to the composite, whose pixels include this frame's fresh draws.
    private readonly FrameClock hueClock = new FrameClock();
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
    private void ApplyGlobalHueRotation(DomeRenderGeneration generation) {
      // Tick every frame (even when off) so re-enabling doesn't jump.
      double frameScale = this.hueClock.Tick();
      double hueSpeed = generation.ShowState.GlobalHueSpeed;
      if (hueSpeed <= 0) {
        return;
      }
      double rate = Math.Pow(10, -hueSpeed);
      double p = this.beat.ProgressThroughMeasure;
      double mod = 3 * p * p - 3 * p + 1;
      double delta = rate * mod * frameScale;
      this.compositor.AdvancePostFrameHue(generation.Plan, delta);
    }

    // Keep the established output façade while the publisher owns both
    // simulator queues, pooled mailboxes, and browser sampling policy.
    public ConcurrentQueue<DomeLEDCommand> SimulatorCommandQueue =>
      this.simulatorPublisher.NativeCommands;
    public ConcurrentQueue<DomeLEDCommand> WebSimulatorCommandQueue =>
      this.simulatorPublisher.WebCommands;
    public const int WebSimulatorFramesPerSecond =
      DomeSimulatorPublisher.WebFramesPerSecond;

    public bool SimulatorHasConsumer {
      get { return this.simulatorPublisher.NativeHasConsumer; }
      set { this.simulatorPublisher.NativeHasConsumer = value; }
    }

    public bool WebSimulatorHasConsumer {
      get { return this.simulatorPublisher.WebHasConsumer; }
      set { this.simulatorPublisher.WebHasConsumer = value; }
    }

    public bool TryTakeSimulatorFrame([NotNullWhen(true)] out int[]? frame) {
      return this.simulatorPublisher.TryTakeNativeFrame(out frame);
    }

    public void ReturnSimulatorFrame(int[]? frame) {
      DomeSimulatorPublisher.ReturnFrame(frame);
    }

    public bool TryTakeWebSimulatorFrame(out int[]? frame) {
      return this.simulatorPublisher.TryTakeWebFrame(out frame);
    }

    public void Flush() {
      this.FlushHardware();
      this.simulatorPublisher.FlushFrame(
        this.OutputSettings.SimulationEnabled);
    }

    // Calibration publishes hardware and logical simulator diagnostics as two
    // independent frames. Candidate navigation uses FlushSimulator without
    // touching OPC; changing the fixed physical selection uses FlushHardware.
    public void FlushHardware() {
      this.EnsureOutputSettingsReconciled();
      this.transport.Flush();
    }

    public void FlushSimulator() {
      this.simulatorPublisher.FlushDiagnostics(
        this.OutputSettings.SimulationEnabled);
    }

    private void SetDevicePixel(int controlBoxIndex, int pixelIndex, int color) {
      int totalPixelIndex =
        controlBoxIndex * (maxStripLength * NumPortsPerBox) + pixelIndex;
      this.transport.SetPixel(totalPixelIndex, color);
    }

    // Raw (identity) device address: which control box and pixel-within-box a
    // strut's LED occupies under the hard-coded strutPositions wiring, ignoring
    // both configured permutations. This is the canonical "what the program
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

    public void SetPixel(int strutIndex, int ledIndex, int color) {
      this.EnsureOutputSettingsReconciled();
      Tuple<int, int> deviceIndexes =
        this.outputMapper.Map(strutIndex, ledIndex);
      this.SetDevicePixel(deviceIndexes.Item1, deviceIndexes.Item2, color);
      this.simulatorPublisher.PublishPixel(
        strutIndex, ledIndex, color,
        this.OutputSettings.SimulationEnabled);
    }

    // Writes only to the raw (unpermuted) control-box address. The calibration
    // visualizer uses this for the fixed physical selection while publishing a
    // different logical candidate to the simulators.
    public void SetPixelRawHardware(
      int strutIndex, int ledIndex, int color
    ) {
      this.EnsureOutputSettingsReconciled();
      Tuple<int, int> deviceIndexes =
        this.GetDeviceIndexesRaw(strutIndex, ledIndex);
      this.SetDevicePixel(deviceIndexes.Item1, deviceIndexes.Item2, color);
    }

    // Publishes a logical strut pixel to native/web simulators without writing
    // an OPC device address.
    public void SetPixelSimulator(int strutIndex, int ledIndex, int color) {
      this.simulatorPublisher.PublishPixel(
        strutIndex, ledIndex, color,
        this.OutputSettings.SimulationEnabled);
    }

    // Compatibility path for the other raw diagnostic visualizers, which want
    // their unpermuted hardware address mirrored at the same logical strut.
    public void SetPixelRaw(int strutIndex, int ledIndex, int color) {
      this.SetPixelRawHardware(strutIndex, ledIndex, color);
      this.SetPixelSimulator(strutIndex, ledIndex, color);
    }

    public DomeFrame MakeDomeFrame() {
      if (this.topology != null) {
        return new DomeFrame(this.topology);
      }
      List<DomeTopologyPixel> pixels = new List<DomeTopologyPixel>();

      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        var leds = LEDDomeOutput.GetNumLEDs(i);
        for (int j = 0; j < leds; j++) {
          var stripPoint = StrutLayoutFactory.GetProjectedLEDPoint(
            i, j, DomeProjection.StripExtents);
          var topDownPoint = StrutLayoutFactory.GetProjectedLEDPoint(
            i, j, DomeProjection.TopDown);
          pixels.Add(new DomeTopologyPixel(
            i, j,
            stripPoint.Item1, stripPoint.Item2,
            topDownPoint.Item1, topDownPoint.Item2));
        }
      }

      this.topology = new DomeTopology(pixels.ToArray());
      return new DomeFrame(this.topology);
    }

    // The strut indices physically carried by one controller cable
    // (boxIndex, half; half 0 = ethernet A = strands 0-3, 1 = B = strands 4-7),
    // under the raw hard-coded wiring. Used by the dome-mapping calibration to
    // write exactly one unpermuted controller block.
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

    // Logical struts belonging to one legacy strip path in a dome-side box.
    // The same geometry is also the raw controller-port shape when boxIndex is
    // a controller box and path is the raw controller port.
    public static List<int> GetStripPathStruts(int boxIndex, int path) {
      var struts = new List<int>();
      if (boxIndex < 0 || boxIndex >= NumDomeBoxes ||
          path < 0 || path >= NumPortsPerBox) {
        return struts;
      }
      int start = 0;
      for (int priorPath = 0; priorPath < path; priorPath++) {
        start += controlBoxStrutOrder[priorPath].Length;
      }
      int end = start + controlBoxStrutOrder[path].Length;
      for (int localIndex = start; localIndex < end; localIndex++) {
        int strutIndex = FindStrutIndex(boxIndex, localIndex);
        if (strutIndex != -1) {
          struts.Add(strutIndex);
        }
      }
      return struts;
    }

    // The struts physically present at one dome cable endpoint after applying
    // the shared per-box port mapping. Calibration diagrams use this so their
    // A/B regions still match the installed paths even when a path crosses the
    // four-port boundary. Invalid mappings render the legacy identity layout.
    public static List<int> GetPhysicalCableStruts(
      int boxIndex, int half, int[]? portMapping
    ) {
      bool valid = IsValidPortMapping(portMapping);
      var struts = new List<int>();
      int firstPort = half * StrandsPerCable;
      int endPort = firstPort + StrandsPerCable;
      for (int port = firstPort; port < endPort; port++) {
        int path = valid && portMapping != null ? portMapping[port] : port;
        struts.AddRange(GetStripPathStruts(boxIndex, path));
      }
      return struts;
    }

    public void WriteBuffer(DomeFrame buffer) {
      this.EnsureOutputSettingsReconciled();
      DomeSimulatorFrameCapture simulatorCapture =
        this.simulatorPublisher.BeginFrame(
          buffer.pixels.Length,
          this.OutputSettings.SimulationEnabled);
      if (!this.transport.CanWrite && !simulatorCapture.Enabled) {
        return;
      }
      // The immutable mapping is snapshotted once for this write; calibration
      // can replace it concurrently without touching the logical frame.
      int stride = maxStripLength * 8;
      DomeOutputMapping mapping = this.outputMapper.Current;
      this.transport.PrepareMapping(mapping);
      for (int i = 0; i < buffer.pixels.Length; i++) {
        LEDDomeOutputPixel pixel = buffer.pixels[i];
        int totalPixelIndex =
          mapping.ControlBoxAt(i) * stride + mapping.PixelWithinBoxAt(i);
        this.transport.SetPixel(totalPixelIndex, pixel.color);
        simulatorCapture.SetColor(i, pixel.color);
      }
      this.simulatorPublisher.CompleteFrame(
        simulatorCapture,
        this.OutputSettings.SimulationEnabled);
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

    public int StrutCount => strutPositions.Length;

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

    // Resolve a relative color slot through the named palette selected by the
    // layer. The palette parameter is an index into config.domePalettes.
    public int GetSingleColor(int index, int paletteIndex = 0) {
      return this.paletteSampler.GetSingleColor(index, paletteIndex);
    }

    public int GetGradientColor(
      int index,
      double pixelPos,
      double focusPos,
      bool wrap,
      int paletteIndex = 0
    ) {
      return this.paletteSampler.GetGradientColor(
        index, pixelPos, focusPos, wrap, paletteIndex);
    }

    public int GetGradientBetweenColors(
      int minIndex,
      int maxIndex,
      double pixelPos,
      double focusPos,
      bool wrap,
      int paletteIndex = 0
    ) {
      return this.paletteSampler.GetGradientBetweenColors(
        minIndex, maxIndex, pixelPos, focusPos, wrap, paletteIndex);
    }
  }

}

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spectrum.Base;
using System.ComponentModel;

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

    private OPCAPI opcAPI;
    private readonly Configuration config;
    private readonly IRuntimeSettingsConfiguration runtimeSettings;
    // Live counters, not config: the OPC send rate is reported here.
    private readonly RuntimeTelemetry telemetry;
    // The tempo service (owned by the Operator, not part of Configuration),
    // read for the beat-synced flash-off in the per-frame color cache.
    private readonly BeatBroadcaster beat;
    private readonly List<Visualizer> visualizers;
    private readonly DomeCompositor compositor;
    private DomeRenderGeneration renderGeneration;
    // Operator-thread-only frame capture. Visualizers, compositor palette
    // effects, and global hue all read through this same accepted generation.
    private DomeRenderGeneration frameRenderGeneration;
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
    private int outputSettingsPending;
    private static readonly int maxStripLength;

    // The dome is wired as 10 "cables": each of the 5 control boxes drives 8
    // strands split across two parallel ethernet cables, A (strands 0-3) and B
    // (strands 4-7). We identify a cable by box*2 + half (half 0 = A, 1 = B), so
    // cable ids run 0..9. Calibration permutes which controller cable feeds which
    // physical dome endpoint; see GetDeviceIndexes and config.domeCableMapping.
    private const int StrandsPerCable = 4;
    public const int NumCables = 10;
    public const int NumDomeBoxes = NumCables / 2;
    public const int NumPortsPerBox = 8;
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
    // portForPath[b, p] is the physical output port in dome box b that the
    // legacy hard-coded strip path p is plugged into. Each row is the inverse
    // of the corresponding operator-friendly physical port -> legacy path
    // configuration value.
    private readonly int[,] portForPath =
      new int[NumDomeBoxes, NumPortsPerBox];
    private DomeOutputMapping outputMapping;
    // Touched only by the operator thread. Comparing mapping snapshots makes an
    // output-map transition clear OPC's persistent next frame exactly when the
    // first frame using that new projection is written.
    private DomeOutputMapping lastWireMapping;
    private DomeTopology topology;

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
      OrientationAngleProvider orientationAngle = null
    ) {
      this.config = config;
      this.runtimeSettings = config as IRuntimeSettingsConfiguration ??
        throw new ArgumentException(
          "LEDDomeOutput requires immutable runtime settings.",
          nameof(config));
      this.telemetry = telemetry;
      this.beat = beat;
      this.visualizers = new List<Visualizer>();
      this.compositor = new DomeCompositor(
        this.MakeDomeFrame, orientationAngle,
        paletteColor: (palette, position) => this.GetGradientBetweenColors(
          0, 7, position, 0, false, palette));
      DomeShowStateSnapshot initialShowState =
        (config as IDomeShowStateConfiguration)?.DomeShowStateSnapshot ??
          DomeShowStateSnapshot.Empty;
      this.renderGeneration = new DomeRenderGeneration(
        RenderPlan.Empty, initialShowState);
      this.frameRenderGeneration = this.renderGeneration;
      this.frameRuntimeSettings =
        this.runtimeSettings.DomeRuntimeFrameSnapshot;
      this.frameOutputSettings =
        this.runtimeSettings.DomeOutputSettingsSnapshot;
      this.RebuildOutputMapping(this.frameOutputSettings);
      this.appliedMappingGeneration =
        this.frameOutputSettings.MappingGeneration;
      this.appliedTransportGeneration =
        this.frameOutputSettings.TransportGeneration;
      this.config.PropertyChanged += ConfigUpdated;
    }

    private static bool IsValidPermutation(
      IReadOnlyList<int> mapping, int count
    ) {
      if (mapping == null || mapping.Count != count) {
        return false;
      }
      var seen = new bool[count];
      foreach (int value in mapping) {
        if (value < 0 || value >= count || seen[value]) {
          return false;
        }
        seen[value] = true;
      }
      return true;
    }

    public static bool IsValidPortMapping(int[] mapping) =>
      IsValidPermutation(mapping, NumPortsPerBox);

    // Rebuilds both wiring permutations and atomically replaces the immutable
    // logical-to-device mapping, so no renderer/compositor frame is mutated.
    // Missing, short, duplicated, or out-of-range permutations fall back to
    // identity independently, so a bad port setting cannot scramble cables (or
    // vice versa).
    private void RebuildOutputMapping(DomeOutputSettingsSnapshot settings) {
      IReadOnlyList<int> cableMapping = settings.CableMapping;
      bool cableMappingValid =
        IsValidPermutation(cableMapping, NumCables);
      for (int controller = 0; controller < NumCables; controller++) {
        int endpoint = cableMappingValid
          ? cableMapping[controller]
          : controller;
        this.controllerForEndpoint[endpoint] = controller;
      }

      ImmutableArray<ImmutableArray<int>> configuredMappings =
        settings.PortMappings;
      bool hasPerBoxMappings =
        configuredMappings.Length == NumDomeBoxes;
      for (int box = 0; box < NumDomeBoxes; box++) {
        IReadOnlyList<int> portMapping = hasPerBoxMappings
          ? configuredMappings[box]
          : null;
        bool portMappingValid =
          IsValidPermutation(portMapping, NumPortsPerBox);
        for (int port = 0; port < NumPortsPerBox; port++) {
          int path = portMappingValid ? portMapping[port] : port;
          this.portForPath[box, path] = port;
        }
      }

      var boxes = new List<int>();
      var pixels = new List<int>();
      for (int strut = 0; strut < GetNumStruts(); strut++) {
        for (int led = 0; led < GetNumLEDs(strut); led++) {
          Tuple<int, int> address = this.GetDeviceIndexes(strut, led);
          boxes.Add(address.Item1);
          pixels.Add(address.Item2);
        }
      }
      System.Threading.Volatile.Write(
        ref this.outputMapping,
        new DomeOutputMapping(boxes.ToArray(), pixels.ToArray()));
    }

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
    private Visualizer[] visualizersArray;
    public Visualizer[] GetVisualizers() {
      return this.visualizersArray
        ?? (this.visualizersArray = this.visualizers.ToArray());
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
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
      DomeOutputSettingsSnapshot settings =
        this.runtimeSettings.DomeOutputSettingsSnapshot;
      if (settings.MappingGeneration != this.appliedMappingGeneration) {
        this.RebuildOutputMapping(settings);
        this.appliedMappingGeneration = settings.MappingGeneration;
      }
      if (settings.TransportGeneration != this.appliedTransportGeneration) {
        this.appliedTransportGeneration = settings.TransportGeneration;
        if (this.opcAPI != null) {
          this.opcAPI.Active = false;
        }
        if (this.active && settings.Enabled) {
          this.initializeOPCAPI(settings);
        }
      }
    }

    private void initializeOPCAPI(DomeOutputSettingsSnapshot settings) {
      var opcAddress = settings.OpcAddress;
      string[] parts = opcAddress.Split(':');
      if (parts.Length < 3) {
        opcAddress += ":0"; // default to channel 0
      }
      this.opcAPI = new OPCAPI(
        opcAddress,
        settings.OutputInSeparateThread,
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
        if (value) {
          Interlocked.Exchange(ref this.outputSettingsPending, 1);
          this.EnsureOutputSettingsReconciled();
          DomeOutputSettingsSnapshot settings =
            this.runtimeSettings.DomeOutputSettingsSnapshot;
          if (settings.Enabled &&
              (this.opcAPI == null || !this.opcAPI.Active)) {
            this.initializeOPCAPI(settings);
          }
        } else if (this.opcAPI != null) {
          this.opcAPI.Active = false;
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
      // opcAPI.OperatorUpdate() sends and before we invalidate the frame color
      // cache. The visualizers ran (into their own buffers) earlier this frame
      // with the cache still valid; Composite reads only their buffers, so the
      // ordering composite -> opc send -> cache invalidate is required. See the
      // GetGradientColor cache note in EnsureFrameColorCache.
      DomeRenderGeneration frameGeneration =
        this.frameRenderGeneration ?? Volatile.Read(ref this.renderGeneration);
      DomeFrame completed = this.compositor.Compose(frameGeneration.Plan);
      if (completed != null) {
        this.WriteBuffer(completed);
        this.Flush();
        this.ApplyGlobalHueRotation(frameGeneration);
      }
      if (this.opcAPI != null) {
         this.opcAPI.OperatorUpdate();
      }
      // The operator runs Visualize() on every winning visualizer and then this
      // OperatorUpdate() once per output, so this marks the end of a frame: drop
      // the per-frame color snapshot so the next frame's first pixel recomputes
      // it. See EnsureFrameColorCache.
      this.frameColorCacheValid = false;
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
      DomeRuntimeFrameSnapshot runtimeSettings = null
    ) {
      DomeRenderGeneration generation =
        Volatile.Read(ref this.renderGeneration) ?? DomeRenderGeneration.Empty;
      this.frameRenderGeneration = generation;
      this.frameRuntimeSettings = runtimeSettings ??
        this.runtimeSettings.DomeRuntimeFrameSnapshot;
      this.frameOutputSettings =
        this.runtimeSettings.DomeOutputSettingsSnapshot;
      this.operatorFrameActive = true;
      this.frameColorCacheValid = false;
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
        this.frameRuntimeSettings.MaxBrightness *
        this.frameRuntimeSettings.Brightness;
      this.frameRenderGeneration ??=
        Volatile.Read(ref this.renderGeneration) ?? DomeRenderGeneration.Empty;
      this.frameColorCacheValid = true;
    }

    // Ordered per-pixel/flush commands for diagnostic visualizers. Normal
    // buffer-rendered frames use the latest-frame mailbox below: the simulator
    // cannot display intermediate frames, so queueing them only creates GC and
    // redundant UI work.
    public ConcurrentQueue<DomeLEDCommand> SimulatorCommandQueue { get; } =
      new ConcurrentQueue<DomeLEDCommand>();

    // Separate from the native queue/mailbox so both simulators can be open
    // without competing for the same latest frame.
    public ConcurrentQueue<DomeLEDCommand> WebSimulatorCommandQueue { get; } =
      new ConcurrentQueue<DomeLEDCommand>();

    private readonly object simulatorFrameGate = new object();
    private volatile bool simulatorHasConsumer;
    private int[] latestSimulatorFrame;
    // Set on the operator thread when WriteBuffer publishes a mailbox frame;
    // Flush then knows it needn't also enqueue a redundant redraw command.
    private bool simulatorFramePublishedSinceFlush;
    private readonly object webSimulatorFrameGate = new object();
    private volatile bool webSimulatorHasConsumer;
    private int[] latestWebSimulatorFrame;
    private bool webSimulatorFramePublishedSinceFlush;
    private long nextWebSimulatorFrameTimestamp;
    public const int WebSimulatorFramesPerSecond = 60;
    private static readonly long WebSimulatorFrameIntervalTicks =
      Stopwatch.Frequency / WebSimulatorFramesPerSecond;

    // True while the simulator window is open. Disabling the consumer also
    // atomically detaches and returns any pending pooled frame.
    public bool SimulatorHasConsumer {
      get { return this.simulatorHasConsumer; }
      set {
        int[] abandoned = null;
        lock (this.simulatorFrameGate) {
          this.simulatorHasConsumer = value;
          if (!value) {
            abandoned = this.latestSimulatorFrame;
            this.latestSimulatorFrame = null;
          }
        }
        if (abandoned != null) {
          ArrayPool<int>.Shared.Return(abandoned);
        }
      }
    }

    public bool WebSimulatorHasConsumer {
      get { return this.webSimulatorHasConsumer; }
      set {
        int[] abandoned = null;
        lock (this.webSimulatorFrameGate) {
          this.webSimulatorHasConsumer = value;
          if (!value) {
            abandoned = this.latestWebSimulatorFrame;
            this.latestWebSimulatorFrame = null;
            this.nextWebSimulatorFrameTimestamp = 0;
          }
        }
        if (abandoned != null) {
          ArrayPool<int>.Shared.Return(abandoned);
        }
        if (!value) {
          this.WebSimulatorCommandQueue.Clear();
        }
      }
    }

    // Only produce simulator output when simulation is on AND a window is
    // consuming it. Without the consumer check, diagnostic commands could grow
    // the queue and normal rendering would keep cycling pooled frame buffers
    // with nobody displaying them.
    private bool ShouldEnqueueDomeCommand =>
      this.OutputSettings.SimulationEnabled && this.SimulatorHasConsumer;

    private bool ShouldEnqueueWebDomeCommand => this.WebSimulatorHasConsumer;

    // The dedicated browser simulator is sampled at 60 FPS. The operator may
    // run at 400 FPS; copying every one of those frames would be pure waste.
    private bool ShouldCaptureWebSimulatorFrame() {
      if (!this.ShouldEnqueueWebDomeCommand) {
        return false;
      }
      long now = Stopwatch.GetTimestamp();
      lock (this.webSimulatorFrameGate) {
        if (!this.webSimulatorHasConsumer ||
            now < this.nextWebSimulatorFrameTimestamp) {
          return false;
        }
        // Advance the target from its previous timestamp so the 400 Hz engine
        // tick quantization does not turn 60 FPS into a steady ~57 FPS. If the
        // producer fell a whole frame behind, reset instead of catch-up bursting.
        if (this.nextWebSimulatorFrameTimestamp == 0 ||
            now - this.nextWebSimulatorFrameTimestamp >=
              WebSimulatorFrameIntervalTicks) {
          this.nextWebSimulatorFrameTimestamp =
            now + WebSimulatorFrameIntervalTicks;
        } else {
          this.nextWebSimulatorFrameTimestamp +=
            WebSimulatorFrameIntervalTicks;
        }
        return true;
      }
    }

    // Replaces the pending normal frame. The producer returns any superseded
    // pooled array immediately; a frame already taken by the UI is owned by the
    // UI until ReturnSimulatorFrame is called.
    private bool PublishSimulatorFrame(int[] frame) {
      int[] superseded = null;
      lock (this.simulatorFrameGate) {
        if (!this.simulatorHasConsumer ||
            !this.OutputSettings.SimulationEnabled) {
          return false;
        }
        superseded = this.latestSimulatorFrame;
        this.latestSimulatorFrame = frame;
      }
      if (superseded != null) {
        ArrayPool<int>.Shared.Return(superseded);
      }
      // A complete frame supersedes any older diagnostic pixels left in the
      // ordered queue (notably when switching out of a diagnostic mode).
      if (!this.SimulatorCommandQueue.IsEmpty) {
        this.SimulatorCommandQueue.Clear();
      }
      return true;
    }

    public bool TryTakeSimulatorFrame(out int[] frame) {
      lock (this.simulatorFrameGate) {
        frame = this.latestSimulatorFrame;
        this.latestSimulatorFrame = null;
        return frame != null;
      }
    }

    public void ReturnSimulatorFrame(int[] frame) {
      if (frame != null) {
        ArrayPool<int>.Shared.Return(frame);
      }
    }

    private bool PublishWebSimulatorFrame(int[] frame) {
      int[] superseded = null;
      lock (this.webSimulatorFrameGate) {
        if (!this.webSimulatorHasConsumer) {
          return false;
        }
        superseded = this.latestWebSimulatorFrame;
        this.latestWebSimulatorFrame = frame;
      }
      if (superseded != null) {
        ArrayPool<int>.Shared.Return(superseded);
      }
      this.WebSimulatorCommandQueue.Clear();
      return true;
    }

    public bool TryTakeWebSimulatorFrame(out int[] frame) {
      lock (this.webSimulatorFrameGate) {
        frame = this.latestWebSimulatorFrame;
        this.latestWebSimulatorFrame = null;
        return frame != null;
      }
    }

    // Hard ceiling on SimulatorCommandQueue depth. The consumer
    // (DomeSimulatorWindow) drains on a ~100Hz UI tick, but the operator thread
    // produces at up to 400Hz, and if the UI thread stalls (window dragged,
    // minimized, slow machine) the queue would otherwise grow without bound. A
    // display only needs recent diagnostic state, so past this cap we drop the
    // oldest commands. Sized above several full diagnostic/calibration frames
    // (~4,500 per-pixel commands plus a Flush each); normal frames never enter
    // this queue.
    private const int SimulatorCommandQueueCap = 20000;

    // Enqueue a simulator command, dropping the oldest queued commands if the
    // backlog has grown past SimulatorCommandQueueCap. TryDequeue here races the
    // consumer's own dequeues, which ConcurrentQueue handles; the consumer
    // tolerates a command being pulled out from under it (see
    // DomeSimulatorWindow.Update).
    private void EnqueueSimulatorCommand(DomeLEDCommand command) {
      EnqueueSimulatorCommand(this.SimulatorCommandQueue, command);
    }

    private void EnqueueWebSimulatorCommand(DomeLEDCommand command) {
      EnqueueSimulatorCommand(this.WebSimulatorCommandQueue, command);
    }

    private static void EnqueueSimulatorCommand(
      ConcurrentQueue<DomeLEDCommand> queue,
      DomeLEDCommand command
    ) {
      queue.Enqueue(command);
      while (
        queue.Count > SimulatorCommandQueueCap &&
        queue.TryDequeue(out _)
      ) {
      }
    }

    public void Flush() {
      this.FlushHardware();
      if (
        this.ShouldEnqueueDomeCommand &&
        !this.simulatorFramePublishedSinceFlush
      ) {
        this.EnqueueSimulatorCommand(
          new DomeLEDCommand() { isFlush = true }
        );
      }
      if (
        this.ShouldEnqueueWebDomeCommand &&
        !this.webSimulatorFramePublishedSinceFlush
      ) {
        this.EnqueueWebSimulatorCommand(
          new DomeLEDCommand() { isFlush = true }
        );
      }
      this.simulatorFramePublishedSinceFlush = false;
      this.webSimulatorFramePublishedSinceFlush = false;
    }

    // Calibration publishes hardware and logical simulator diagnostics as two
    // independent frames. Candidate navigation uses FlushSimulator without
    // touching OPC; changing the fixed physical selection uses FlushHardware.
    public void FlushHardware() {
      this.EnsureOutputSettingsReconciled();
      if (this.opcAPI != null) {
        this.opcAPI.Flush();
      }
    }

    public void FlushSimulator() {
      if (this.ShouldEnqueueDomeCommand) {
        this.EnqueueSimulatorCommand(
          new DomeLEDCommand() { isFlush = true }
        );
      }
      if (this.ShouldEnqueueWebDomeCommand) {
        this.EnqueueWebSimulatorCommand(
          new DomeLEDCommand() { isFlush = true }
        );
      }
    }

    private void SetDevicePixel(int controlBoxIndex, int pixelIndex, int color) {
      lock (this.visualizers) {
        if (this.opcAPI != null) {
          int totalPixelIndex =
            controlBoxIndex * (maxStripLength * NumPortsPerBox) + pixelIndex;
          this.opcAPI.SetPixel(totalPixelIndex, color);
        }
      }
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

    // Mapped device address. First relocate the legacy strip path onto the
    // configured physical port within its dome box. Then use the calibrated
    // cable permutation to find the controller cable feeding that port's four-
    // strand endpoint. This order lets an arbitrary eight-port permutation
    // compose with cable swaps, including ports moved between halves A and B.
    // Identity mappings reproduce GetDeviceIndexesRaw exactly.
    private Tuple<int, int> GetDeviceIndexes(int strutIndex, int ledIndex) {
      Tuple<int, int> raw = this.GetDeviceIndexesRaw(strutIndex, ledIndex);
      int box = raw.Item1;
      int legacyPath = raw.Item2 / maxStripLength;
      int offsetWithinStrand = raw.Item2 - legacyPath * maxStripLength;
      int physicalPort = this.portForPath[box, legacyPath];
      int half = physicalPort / StrandsPerCable;
      int strandWithinCable = physicalPort % StrandsPerCable;
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
      this.EnsureOutputSettingsReconciled();
      Tuple<int, int> deviceIndexes = this.GetDeviceIndexes(strutIndex, ledIndex);
      this.SetDevicePixel(deviceIndexes.Item1, deviceIndexes.Item2, color);

      if (this.ShouldEnqueueDomeCommand) {
        this.EnqueueSimulatorCommand(new DomeLEDCommand() {
          strutIndex = strutIndex,
          ledIndex = ledIndex,
          color = color,
        });
      }
      if (this.ShouldEnqueueWebDomeCommand) {
        this.EnqueueWebSimulatorCommand(new DomeLEDCommand() {
          strutIndex = strutIndex,
          ledIndex = ledIndex,
          color = color,
        });
      }
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
      if (this.ShouldEnqueueDomeCommand) {
        this.EnqueueSimulatorCommand(new DomeLEDCommand() {
          strutIndex = strutIndex,
          ledIndex = ledIndex,
          color = color,
        });
      }
      if (this.ShouldEnqueueWebDomeCommand) {
        this.EnqueueWebSimulatorCommand(new DomeLEDCommand() {
          strutIndex = strutIndex,
          ledIndex = ledIndex,
          color = color,
        });
      }
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
      int boxIndex, int half, int[] portMapping
    ) {
      bool valid = IsValidPortMapping(portMapping);
      var struts = new List<int>();
      int firstPort = half * StrandsPerCable;
      int endPort = firstPort + StrandsPerCable;
      for (int port = firstPort; port < endPort; port++) {
        int path = valid ? portMapping[port] : port;
        struts.AddRange(GetStripPathStruts(boxIndex, path));
      }
      return struts;
    }

    public void WriteBuffer(DomeFrame buffer) {
      this.EnsureOutputSettingsReconciled();
      // Snapshot opcAPI once instead of null-checking + locking per pixel (P4).
      // The visualizers list is only mutated at registration time, so the
      // per-pixel lock in SetDevicePixel guarded nothing here.
      OPCAPI opcAPI = this.opcAPI;
      bool simulationEnabled = this.ShouldEnqueueDomeCommand;
      // Even when this particular normal frame is skipped by the 60 FPS
      // sampler, Flush must not enqueue an empty diagnostic redraw command.
      // WriteBuffer itself establishes that this is the normal-buffer path.
      if (this.ShouldEnqueueWebDomeCommand) {
        this.webSimulatorFramePublishedSinceFlush = true;
      }
      bool webSimulationEnabled = this.ShouldCaptureWebSimulatorFrame();
      if (opcAPI == null && !simulationEnabled && !webSimulationEnabled) {
        return;
      }
      // The immutable mapping is snapshotted once for this write; calibration
      // can replace it concurrently without touching the logical frame.
      int stride = maxStripLength * 8;
      DomeOutputMapping mapping = System.Threading.Volatile.Read(
        ref this.outputMapping);
      if (opcAPI != null &&
          !ReferenceEquals(mapping, this.lastWireMapping)) {
        opcAPI.ClearPixels();
        this.lastWireMapping = mapping;
      }
      // Rent a whole-frame snapshot for the latest-frame mailbox. If the UI has
      // not consumed the previous frame, PublishSimulatorFrame replaces and
      // returns it instead of building a backlog.
      int[] frame = simulationEnabled
        ? ArrayPool<int>.Shared.Rent(buffer.pixels.Length) : null;
      int[] webFrame = webSimulationEnabled
        ? ArrayPool<int>.Shared.Rent(buffer.pixels.Length) : null;
      for (int i = 0; i < buffer.pixels.Length; i++) {
        LEDDomeOutputPixel pixel = buffer.pixels[i];
        int totalPixelIndex =
          mapping.ControlBoxAt(i) * stride + mapping.PixelWithinBoxAt(i);
        if (opcAPI != null) {
          opcAPI.SetPixel(totalPixelIndex, pixel.color);
        }
        if (simulationEnabled) {
          frame[i] = pixel.color;
        }
        if (webSimulationEnabled) {
          webFrame[i] = pixel.color;
        }
      }
      if (simulationEnabled) {
        if (this.PublishSimulatorFrame(frame)) {
          this.simulatorFramePublishedSinceFlush = true;
        } else {
          ArrayPool<int>.Shared.Return(frame);
        }
      }
      if (webSimulationEnabled) {
        if (this.PublishWebSimulatorFrame(webFrame)) {
          this.webSimulatorFramePublishedSinceFlush = true;
        } else {
          ArrayPool<int>.Shared.Return(webFrame);
        }
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
      this.EnsureFrameColorCache();
      if (this.frameFlashedOff) {
        return 0x000000;
      }
      DomePaletteSnapshot palette =
        this.frameRenderGeneration.ShowState.ResolvePalette(paletteIndex);
      return LEDColor.ScaleColor(
        palette == null ? 0x000000 : palette.GetSingleColor(index),
        this.frameBrightness
      );
    }

    public int GetGradientColor(
      int index,
      double pixelPos,
      double focusPos,
      bool wrap,
      int paletteIndex = 0
    ) {
      this.EnsureFrameColorCache();
      if (this.frameFlashedOff) {
        return 0x000000;
      }
      DomePaletteSnapshot palette =
        this.frameRenderGeneration.ShowState.ResolvePalette(paletteIndex);
      if (
        palette == null ||
        index < 0 || index >= palette.Colors.Length ||
        palette.Colors[index] == null
      ) {
        return 0x000000;
      }
      if (!palette.Colors[index].Value.IsGradient) {
        return LEDColor.ScaleColor(
          palette.GetSingleColor(index),
          this.frameBrightness
        );
      }
      return LEDColor.ScaleColor(
        palette.GetGradientColor(index, pixelPos, focusPos, wrap),
        this.frameBrightness
      );
    }

    public int GetGradientBetweenColors(
      int minIndex,
      int maxIndex,
      double pixelPos,
      double focusPos,
      bool wrap,
      int paletteIndex = 0
    ) {
      // Return a color evenly scaled between min index and max index, based on the pixel position.
      if (double.IsNaN(pixelPos) || double.IsInfinity(pixelPos) ||
          pixelPos < 0 || pixelPos > 1) {
        throw new ArgumentException("Pixel Position out of range: " + pixelPos.ToString());
      }
      if (minIndex < 0) {
        throw new ArgumentOutOfRangeException(
          nameof(minIndex), "Minimum color index cannot be negative.");
      }
      if (maxIndex <= minIndex) {
        throw new ArgumentException(
          "Maximum color index must be greater than minimum color index.",
          nameof(maxIndex));
      }
      if (paletteIndex < 0) {
        throw new ArgumentOutOfRangeException(
          nameof(paletteIndex), "Palette index cannot be negative.");
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
      DomePaletteSnapshot palette =
        this.frameRenderGeneration.ShowState.ResolvePalette(paletteIndex);
      if (
        palette == null ||
        palette.Colors.Length <= minColorIdx ||
        palette.Colors[minColorIdx] == null
      ) {
        return 0x000000;
      }
      if (
        palette.Colors.Length <= maxColorIdx ||
        palette.Colors[maxColorIdx] == null
      ) {
        return 0x000000;
      }
      if (!palette.Colors[minColorIdx].Value.IsGradient) {
        return this.GetSingleColor(minColorIdx, paletteIndex);
      }
      // Blend Color1 of the two adjacent slots. Read the palette directly
      // (unscaled) and apply frameBrightness exactly once at the end — routing
      // the endpoints through this.GetSingleColor would pre-scale each by
      // frameBrightness, making the result quadratic in the brightness slider.
      var color = new DomeColorSnapshot(
        palette.GetSingleColor(minColorIdx),
        palette.GetSingleColor(maxColorIdx),
        true);
      return LEDColor.ScaleColor(
        color.GradientColor(scaledPixelPos, focusPos, wrap),
        this.frameBrightness
      );
    }
  }

}

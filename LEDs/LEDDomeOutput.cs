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

  public class LEDDomeOutput : Output, DomeRenderContext {

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
    private readonly DomeOutputMapper outputMapper;

    public const int NumCables = DomeOutputMapper.NumCables;
    public const int NumDomeBoxes = DomeOutputMapper.NumDomeBoxes;
    public const int NumPortsPerBox = DomeOutputMapper.NumPortsPerBox;

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
        DomeWiringLayout.MaxStripLength,
        () => DomeWiringLayout.StrutCount,
        DomeWiringLayout.GetLedCount,
        DomeWiringLayout.GetRawAddress);
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
        controlBoxIndex * DomeWiringLayout.ControlBoxPixelCount + pixelIndex;
      this.transport.SetPixel(totalPixelIndex, color);
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
        DomeWiringLayout.GetRawAddress(strutIndex, ledIndex);
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
      return DomeWiringLayout.MakeFrame();
    }

    // The strut indices physically carried by one controller cable
    // (boxIndex, half; half 0 = ethernet A = strands 0-3, 1 = B = strands 4-7),
    // under the raw hard-coded wiring. Used by the dome-mapping calibration to
    // write exactly one unpermuted controller block.
    public static List<int> GetControllerCableStruts(int boxIndex, int half) {
      return DomeWiringLayout.GetControllerCableStruts(boxIndex, half);
    }

    // Logical struts belonging to one legacy strip path in a dome-side box.
    // The same geometry is also the raw controller-port shape when boxIndex is
    // a controller box and path is the raw controller port.
    public static List<int> GetStripPathStruts(int boxIndex, int path) {
      return DomeWiringLayout.GetStripPathStruts(boxIndex, path);
    }

    // The struts physically present at one dome cable endpoint after applying
    // the shared per-box port mapping. Calibration diagrams use this so their
    // A/B regions still match the installed paths even when a path crosses the
    // four-port boundary. Invalid mappings render the legacy identity layout.
    public static List<int> GetPhysicalCableStruts(
      int boxIndex, int half, int[]? portMapping
    ) {
      return DomeWiringLayout.GetPhysicalCableStruts(
        boxIndex, half, portMapping);
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
      int stride = DomeWiringLayout.ControlBoxPixelCount;
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
      return DomeWiringLayout.FindStrutIndex(
        controlBoxIndex, controlBoxStrutIndex);
    }

    public static int GetNumStruts() {
      return DomeWiringLayout.StrutCount;
    }

    public int StrutCount => DomeWiringLayout.StrutCount;

    public static int GetNumLEDs(int strutIndex) {
      return DomeWiringLayout.GetLedCount(strutIndex);
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

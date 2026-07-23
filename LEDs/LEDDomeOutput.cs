using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Spectrum.Base;

namespace Spectrum.LEDs {

  public class LEDDomeOutput : Output, DomeRenderContext {

    private readonly IRuntimeSettingsConfiguration runtimeSettings;
    private readonly DomeOpcTransport transport;
    private readonly DomeSimulatorPublisher simulatorPublisher =
      new DomeSimulatorPublisher();
    private readonly DomeRenderState renderState;
    private readonly DomeRenderPipeline renderPipeline;
    private readonly DomeOutputSettingsCoordinator outputSettingsCoordinator;

    internal WaitHandle? PendingOpcConnectWaitHandle =>
      this.transport.PendingConnectWaitHandle;
    internal long AppliedMappingGeneration =>
      this.outputSettingsCoordinator.AppliedMappingGeneration;
    internal long AppliedTransportGeneration =>
      this.outputSettingsCoordinator.AppliedTransportGeneration;
    internal event Action? OutputSettingsApplied {
      add { this.outputSettingsCoordinator.SettingsApplied += value; }
      remove { this.outputSettingsCoordinator.SettingsApplied -= value; }
    }
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
      DomeShowStateSnapshot initialShowState =
        (config as IDomeShowStateConfiguration)?.DomeShowStateSnapshot ??
          DomeShowStateSnapshot.Empty;
      this.renderState = new DomeRenderState(
        beat,
        new DomeRenderGeneration(RenderPlan.Empty, initialShowState),
        () => this.runtimeSettings.DomeRuntimeFrameSnapshot,
        () => this.runtimeSettings.DomeOutputSettingsSnapshot);
      this.renderPipeline = new DomeRenderPipeline(
        this.MakeDomeFrame,
        this.PublishCompletedFrame,
        orientationAngle,
        (palette, position) =>
          this.renderState.PaletteSampler.GetGradientBetweenColors(
            0, 7, position, 0, false, palette),
        () => beat.ProgressThroughMeasure);
      this.outputSettingsCoordinator = new DomeOutputSettingsCoordinator(
        config,
        this.runtimeSettings,
        this.outputMapper,
        this.transport);
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
      this.renderPipeline.RegisterVisualizer(visualizer);
    }

    public void UnregisterVisualizer(Visualizer visualizer) {
      this.renderPipeline.UnregisterVisualizer(visualizer);
    }

    public Visualizer[] GetVisualizers() {
      return this.renderPipeline.GetVisualizers();
    }

    public bool Active {
      get { return this.outputSettingsCoordinator.Active; }
      set { this.outputSettingsCoordinator.Active = value; }
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
      this.outputSettingsCoordinator.EnsureApplied();
      DomeRenderGeneration frameGeneration =
        this.renderState.FrameGeneration;
      this.renderPipeline.Render(frameGeneration);
      this.transport.OperatorUpdate();
      this.renderState.EndFrame();
    }

    // Called before DomeRenderPipeline advances persistent layer trails. The
    // completed frame reaches hardware and simulators while the render state's
    // immutable operator-frame capture is still active.
    private void PublishCompletedFrame(DomeFrame completed) {
      this.WriteBuffer(completed);
      this.Flush();
    }

    // The Operator accepts the compiled plan and every persisted value it can
    // consume with one reference write. A failed candidate leaves the complete
    // previous generation live.
    public void PublishRenderGeneration(DomeRenderGeneration generation) {
      if (generation == null) {
        throw new ArgumentNullException(nameof(generation));
      }
      this.renderState.Publish(generation);
      // Preserve DomeCompositor's standalone Plan/Compose API for diagnostics
      // and tests; production composition receives the captured frame plan.
      this.renderPipeline.Publish(generation.Plan);
    }

    // Updates only the immutable show-state half while retaining the accepted
    // plan. Compare/exchange prevents a concurrent plan reconciliation from
    // being overwritten by a palette/global-only publication.
    public void PublishShowState(DomeShowStateSnapshot showState) {
      this.renderState.PublishShowState(showState);
    }

    public DomeShowStateSnapshot BeginOperatorFrame(
      DomeRuntimeFrameSnapshot? runtimeSettings = null
    ) {
      return this.renderState.BeginFrame(runtimeSettings);
    }

    // Compatibility helper for direct compositor/output tests.
    public void PublishRenderPlan(RenderPlan plan) {
      DomeRenderGeneration current = this.renderState.CurrentGeneration;
      this.PublishRenderGeneration(new DomeRenderGeneration(
        plan ?? RenderPlan.Empty, current.ShowState));
    }

    public RenderPlan RenderPlan => this.renderState.Plan;

    public DomeShowStateSnapshot ShowState => this.renderState.ShowState;

    // Diagnostics execute on the operator thread and share this exact capture
    // with normal layer renderers and output brightness calculation.
    public DomeRuntimeFrameSnapshot RuntimeFrameSettings =>
      this.renderState.RuntimeSettings;
    public DomeOutputSettingsSnapshot OutputSettings =>
      this.renderState.OutputSettings;

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
      this.outputSettingsCoordinator.EnsureApplied();
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
      this.outputSettingsCoordinator.EnsureApplied();
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
      this.outputSettingsCoordinator.EnsureApplied();
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
      this.outputSettingsCoordinator.EnsureApplied();
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
      return this.renderState.PaletteSampler.GetSingleColor(
        index, paletteIndex);
    }

    public int GetGradientColor(
      int index,
      double pixelPos,
      double focusPos,
      bool wrap,
      int paletteIndex = 0
    ) {
      return this.renderState.PaletteSampler.GetGradientColor(
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
      return this.renderState.PaletteSampler.GetGradientBetweenColors(
        minIndex, maxIndex, pixelPos, focusPos, wrap, paletteIndex);
    }
  }

}

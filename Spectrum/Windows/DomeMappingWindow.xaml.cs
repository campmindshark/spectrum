using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Spectrum.LEDs;
using Spectrum.Web;
using WColor = System.Windows.Media.Color;

namespace Spectrum {

  // Native presentation of the same authoritative, two-stage calibration used
  // by the maintenance web UI. Controller cables are matched first, then each
  // box's eight physical ports are matched to logical strip paths. Both clients
  // share the controller and advisory lease, so they cannot drive conflicting
  // physical output or persist partial mappings independently.
  public partial class DomeMappingWindow : Window {
    private const double DomeScale = 700;
    private const double DomeOffset = 20;
    private const string NativeHolderName = "Native GUI";

    private readonly DomeCalibrationController controller;
    private readonly AdvisoryLockManager locks;
    private readonly Line[] strutLines =
      new Line[LEDDomeOutput.GetNumStruts()];
    private readonly DispatcherTimer leaseHeartbeat;
    private readonly Brush baseStrutBrush =
      new SolidColorBrush(WColor.FromRgb(0x34, 0x39, 0x44));

    private DomeCalibrationController.CalibrationState state;
    private DomeProjection projection = DomeProjection.TopDown;
    private string leaseToken;
    private bool actionInFlight;

    public DomeMappingWindow(
      DomeCalibrationController controller,
      AdvisoryLockManager locks
    ) {
      this.InitializeComponent();
      this.controller = controller ??
        throw new ArgumentNullException(nameof(controller));
      this.locks = locks ?? throw new ArgumentNullException(nameof(locks));
      this.BuildDiagram();
      this.leaseHeartbeat = new DispatcherTimer {
        Interval = TimeSpan.FromSeconds(5),
      };
      this.leaseHeartbeat.Tick += this.RenewNativeLease;
    }

    private void WindowLoaded(object sender, RoutedEventArgs e) {
      this.state = this.controller.State();
      this.statusLabel.Text = "";
      this.RenderState();
    }

    private void WindowClosed(object sender, EventArgs e) {
      this.leaseHeartbeat.Stop();
      this.leaseHeartbeat.Tick -= this.RenewNativeLease;
      if (this.OwnsNativeLease()) {
        // Cancel is an in-memory state transition and its task is already
        // complete when returned, so this restores normal dome rendering before
        // the native window disappears without blocking on the Dispatcher.
        this.controller.CancelAsync().GetAwaiter().GetResult();
      }
      this.ReleaseNativeLease();
    }

    private bool OwnsNativeLease() =>
      this.leaseToken != null && this.locks.HoldsLock(
        LockPolicy.DomeCalibration, this.leaseToken);

    private bool AcquireNativeLease() {
      if (this.OwnsNativeLease()) {
        return true;
      }
      this.leaseToken = this.locks.TryAcquire(
        LockPolicy.DomeCalibration,
        NativeHolderName,
        out AdvisoryLockManager.LockInfo current);
      if (this.leaseToken == null) {
        string holder = current?.holderName ?? "another client";
        this.ShowError("Calibration is currently controlled by " + holder + ".");
        return false;
      }
      this.leaseHeartbeat.Start();
      return true;
    }

    private void ReleaseNativeLease() {
      this.leaseHeartbeat.Stop();
      if (this.leaseToken != null) {
        this.locks.TryRelease(
          LockPolicy.DomeCalibration, this.leaseToken);
        this.leaseToken = null;
      }
    }

    private void RenewNativeLease(object sender, EventArgs e) {
      if (this.leaseToken == null) {
        return;
      }
      if (this.locks.TryRenew(
            LockPolicy.DomeCalibration, this.leaseToken)) {
        return;
      }
      this.leaseToken = null;
      this.leaseHeartbeat.Stop();
      this.state = this.controller.State();
      this.ShowError(
        "The native calibration lease expired. This view is now read-only.");
      this.RenderState();
    }

    private async Task BeginCalibrationAsync() {
      if (this.actionInFlight || !this.AcquireNativeLease()) {
        return;
      }
      this.actionInFlight = true;
      this.RenderState();
      try {
        this.state = await this.controller.StartAsync();
        this.statusLabel.Text = "";
      } catch (Exception error) {
        this.ReleaseNativeLease();
        this.state = this.controller.State();
        this.ShowError(error.Message);
      } finally {
        this.actionInFlight = false;
        this.RenderState();
      }
    }

    private async Task RunAction(
      Func<Task<DomeCalibrationController.CalibrationState>> action
    ) {
      if (this.actionInFlight || !this.OwnsNativeLease()) {
        return;
      }
      this.actionInFlight = true;
      this.RenderState();
      try {
        this.state = await action();
        this.statusLabel.Text = "";
      } catch (Exception error) {
        this.state = this.controller.State();
        this.ShowError(error.Message);
      } finally {
        this.actionInFlight = false;
        this.RenderState();
      }
    }

    private async Task SaveAsync() {
      if (this.actionInFlight || !this.OwnsNativeLease()) {
        return;
      }
      this.actionInFlight = true;
      this.RenderState();
      try {
        (bool ok, string error, var next) =
          await this.controller.SaveAsync();
        this.state = next;
        if (!ok) {
          this.ShowError(error);
          return;
        }
        this.ReleaseNativeLease();
        this.ShowSuccess("All dome cable and strip mappings saved.");
      } catch (Exception error) {
        this.state = this.controller.State();
        this.ShowError(error.Message);
      } finally {
        this.actionInFlight = false;
        this.RenderState();
      }
    }

    private async Task CancelAsync() {
      if (this.actionInFlight || !this.OwnsNativeLease()) {
        return;
      }
      this.actionInFlight = true;
      this.RenderState();
      try {
        this.state = await this.controller.CancelAsync();
        this.ReleaseNativeLease();
        this.ShowSuccess("Calibration cancelled; saved mappings were unchanged.");
      } catch (Exception error) {
        this.state = this.controller.State();
        this.ShowError(error.Message);
      } finally {
        this.actionInFlight = false;
        this.RenderState();
      }
    }

    private void RenderState() {
      if (this.state == null) {
        return;
      }
      bool ownsLock = this.OwnsNativeLease();
      bool actionsEnabled = ownsLock && !this.actionInFlight;
      this.actionsPanel.Children.Clear();
      this.boxSelector.Children.Clear();
      this.cableReadout.Text = this.state.cableReadout ?? "";
      this.stripReadout.Text = this.state.stripReadout ?? "";

      if (!this.state.active) {
        this.introLabel.Text = this.state.hasSavedMapping
          ? "Saved mappings are loaded as the initial guesses for a new calibration."
          : "Missing or invalid mappings will start from identity guesses.";
        this.lockWarning.Visibility = Visibility.Collapsed;
        this.frameSummary.Visibility = Visibility.Collapsed;
        this.boxSelector.Visibility = Visibility.Collapsed;
        this.progressLabel.Text =
          "Stage 1 maps controller cables; Stage 2 maps each box's physical ports.";
        this.AddAction(
          "Start two-stage calibration",
          this.BeginCalibrationAsync,
          !this.actionInFlight,
          primary: true);
        this.AddAction(
          "Close", () => {
            this.Close();
            return Task.CompletedTask;
          }, !this.actionInFlight);
        this.RefreshPreview();
        return;
      }

      this.introLabel.Text =
        "Match the logical candidate to the fixed output on the physical dome.";
      this.frameSummary.Visibility = Visibility.Visible;
      this.UpdateFrameSummary();
      this.lockWarning.Visibility = ownsLock
        ? Visibility.Collapsed : Visibility.Visible;
      if (!ownsLock) {
        AdvisoryLockManager.LockInfo held =
          this.locks.Get(LockPolicy.DomeCalibration);
        this.lockWarning.Text = held == null
          ? "Calibration is active without a current lease; waiting for it to be cancelled."
          : "Calibration is active in " + held.holderName +
            ". This view is read-only.";
      }

      if (this.state.stage == "cables") {
        this.RenderCableStage(actionsEnabled);
      } else if (this.state.stage == "strips") {
        this.RenderStripStage(actionsEnabled);
      } else if (this.state.stage == "review") {
        this.RenderReviewStage(actionsEnabled);
      }
      this.RefreshPreview();
    }

    private void RenderCableStage(bool enabled) {
      this.boxSelector.Visibility = Visibility.Collapsed;
      if (this.state.currentStep < this.state.numCables) {
        this.progressLabel.Text =
          "Stage 1 of 2 · Controller cable " +
          (this.state.currentStep + 1) + " of " + this.state.numCables +
          ". Navigate until the simulator matches the area lit on the real dome.";
        this.AddAction("Previous candidate",
          () => this.RunAction(() => this.controller.NavigateAsync(-1)), enabled);
        this.AddAction("Next candidate",
          () => this.RunAction(() => this.controller.NavigateAsync(1)), enabled);
        this.AddAction("Matches actual dome",
          () => this.RunAction(() => this.controller.ConfirmAsync()), enabled,
          primary: true);
        this.AddAction("Back one output",
          () => this.RunAction(() => this.controller.BackAsync()),
          enabled && this.state.currentStep > 0);
      } else {
        this.progressLabel.Text =
          "Stage 1 complete. Review the cable mapping below before continuing.";
        this.AddAction("Continue to strip calibration",
          () => this.RunAction(() => this.controller.ConfirmAsync()), enabled,
          primary: true);
        this.AddAction("Back one output",
          () => this.RunAction(() => this.controller.BackAsync()), enabled);
      }
      this.AddAction("Cancel", this.CancelAsync, enabled, danger: true);
    }

    private void RenderStripStage(bool enabled) {
      this.BuildBoxSelector(enabled);
      int box = this.state.selectedBox;
      int step = this.state.stripSteps[box];
      int count = this.state.stripMappings[box].Length;
      if (step < count) {
        this.progressLabel.Text =
          "Stage 2 of 2 · Box " + (box + 1) + ", physical port " +
          (step + 1) + " of " + count +
          ". Match the simulator path to the fixed physical output.";
        this.AddAction("Previous candidate",
          () => this.RunAction(() => this.controller.NavigateAsync(-1)), enabled);
        this.AddAction("Next candidate",
          () => this.RunAction(() => this.controller.NavigateAsync(1)), enabled);
        this.AddAction("Matches actual dome",
          () => this.RunAction(() => this.controller.ConfirmAsync()), enabled,
          primary: true);
        this.AddAction("Back one output",
          () => this.RunAction(() => this.controller.BackAsync()),
          enabled && step > 0);
      } else {
        this.progressLabel.Text = "Box " + (box + 1) + " is " +
          this.state.boxStatuses[box] + ".";
        int next = this.NextUnfinishedBox();
        if (next >= 0) {
          int nextBox = next;
          this.AddAction("Continue with Box " + (nextBox + 1),
            () => this.RunAction(
              () => this.controller.SelectBoxAsync(nextBox)), enabled,
            primary: true);
        }
        this.AddAction("Recalibrate this box",
          () => this.RunAction(
            () => this.controller.RecalibrateBoxAsync(box)), enabled);
      }
      if (this.state.canApplyBoxOne) {
        this.AddAction("Apply Box 1 mapping to every box",
          () => this.RunAction(() => this.controller.ApplyBoxOneAsync()), enabled);
      }
      this.AddAction("Cancel", this.CancelAsync, enabled, danger: true);
    }

    private void RenderReviewStage(bool enabled) {
      this.BuildBoxSelector(enabled);
      this.progressLabel.Text = this.state.saveable
        ? "Both stages are complete. Review the live mappings, then save them together."
        : "The draft is not complete; recalibrate an unfinished box.";
      this.AddAction("Save all mappings", this.SaveAsync,
        enabled && this.state.saveable, primary: true);
      int box = this.state.selectedBox;
      this.AddAction("Recalibrate Box " + (box + 1),
        () => this.RunAction(
          () => this.controller.RecalibrateBoxAsync(box)), enabled);
      this.AddAction("Cancel", this.CancelAsync, enabled, danger: true);
    }

    private void BuildBoxSelector(bool enabled) {
      this.boxSelector.Visibility = Visibility.Visible;
      for (int box = 0; box < this.state.boxStatuses.Length; box++) {
        int selectedBox = box;
        var item = new Button {
          Content = "Box " + (box + 1) + " · " + this.state.boxStatuses[box],
          Margin = new Thickness(0, 0, 6, 6),
          Padding = new Thickness(7, 3, 7, 3),
          IsEnabled = enabled,
        };
        if (box == this.state.selectedBox) {
          item.FontWeight = FontWeights.Bold;
          item.BorderBrush = (Brush)this.FindResource("WarningBrush");
          item.BorderThickness = new Thickness(2);
        }
        item.Click += async (_, __) => await this.RunAction(
          () => this.controller.SelectBoxAsync(selectedBox));
        this.boxSelector.Children.Add(item);
      }
    }

    private Button AddAction(
      string text,
      Func<Task> action,
      bool enabled,
      bool primary = false,
      bool danger = false
    ) {
      var button = new Button {
        Content = text,
        Margin = new Thickness(0, 0, 7, 7),
        Padding = new Thickness(9, 4, 9, 4),
        IsEnabled = enabled,
      };
      if (primary) {
        button.FontWeight = FontWeights.Bold;
      }
      if (danger) {
        button.Foreground = (Brush)this.FindResource("ErrorBrush");
      }
      button.Click += async (_, __) => await action();
      this.actionsPanel.Children.Add(button);
      return button;
    }

    private int NextUnfinishedBox() {
      for (int offset = 1; offset <= this.state.boxStatuses.Length; offset++) {
        int box = (this.state.selectedBox + offset) %
          this.state.boxStatuses.Length;
        if (this.state.stripSteps[box] <
            this.state.stripMappings[box].Length) {
          return box;
        }
      }
      return -1;
    }

    private void UpdateFrameSummary() {
      if (this.state.stage == "cables" &&
          this.state.currentStep < this.state.numCables) {
        this.hardwareLabel.Text = "Physical output held: " +
          this.state.cableLabels[this.state.rawControllerCable];
        this.simulatorLabel.Text = "Simulator candidate: Dome endpoint " +
          this.state.endpointLabels[this.state.simulatorEndpoint];
        return;
      }
      if (this.state.stage == "strips" &&
          this.state.rawControllerPort >= 0) {
        this.hardwareLabel.Text = "Physical output held: Controller Box " +
          (this.state.rawControllerBox + 1) + ", Port " +
          (this.state.rawControllerPort + 1);
        this.simulatorLabel.Text = "Simulator candidate: Box " +
          (this.state.simulatorBox + 1) + ", Strip path " +
          (this.state.simulatorPath + 1);
        return;
      }
      this.hardwareLabel.Text = "Physical output: blank for review";
      this.simulatorLabel.Text = "Simulator candidate: blank for review";
    }

    private void BuildDiagram() {
      for (int strut = 0; strut < this.strutLines.Length; strut++) {
        var line = new Line {
          Stroke = this.baseStrutBrush,
          StrokeThickness = 2,
          StrokeStartLineCap = PenLineCap.Round,
          StrokeEndLineCap = PenLineCap.Round,
          Tag = strut,
        };
        this.strutLines[strut] = line;
        this.candidateCanvas.Children.Add(line);
      }
      this.UpdateProjection();
    }

    private void ProjectionClicked(object sender, RoutedEventArgs e) {
      this.projection = this.projection == DomeProjection.TopDown
        ? DomeProjection.StripExtents
        : DomeProjection.TopDown;
      this.UpdateProjection();
    }

    private void UpdateProjection() {
      bool topDown = this.projection == DomeProjection.TopDown;
      this.projectionButton.Content = topDown
        ? "View: Real top-down"
        : "View: Strip extents";
      this.projectionButton.ToolTip = topDown
        ? "Show the full LED strip extents"
        : "Foreshorten the dome as it appears from directly above";
      for (int strut = 0; strut < this.strutLines.Length; strut++) {
        Point p0 = this.Project(
          StrutLayoutFactory.GetProjectedPoint(strut, 0, this.projection));
        Point p1 = this.Project(
          StrutLayoutFactory.GetProjectedPoint(strut, 1, this.projection));
        Line line = this.strutLines[strut];
        line.X1 = p0.X;
        line.Y1 = p0.Y;
        line.X2 = p1.X;
        line.Y2 = p1.Y;
      }
    }

    private Point Project(Tuple<double, double> normalized) => new Point(
      normalized.Item1 * DomeScale + DomeOffset,
      normalized.Item2 * DomeScale + DomeOffset);

    private void RefreshPreview() {
      IEnumerable<int> candidate = Array.Empty<int>();
      string label = "Preview is blank";
      if (this.state.active && this.state.stage == "cables" &&
          this.state.simulatorEndpoint >= 0) {
        int endpoint = this.state.simulatorEndpoint;
        int box = endpoint / 2;
        candidate = LEDDomeOutput.GetPhysicalCableStruts(
          box, endpoint % 2, this.state.savedPortMappings[box]);
        label = "Dome endpoint " + this.state.endpointLabels[endpoint];
      } else if (this.state.active && this.state.stage == "strips" &&
                 this.state.simulatorBox >= 0 &&
                 this.state.simulatorPath >= 0) {
        candidate = LEDDomeOutput.GetStripPathStruts(
          this.state.simulatorBox, this.state.simulatorPath);
        label = "Box " + (this.state.simulatorBox + 1) +
          " · Strip path " + (this.state.simulatorPath + 1);
      }

      var highlighted = new HashSet<int>(candidate);
      Brush highlightBrush = (Brush)this.FindResource("WarningBrush");
      for (int strut = 0; strut < this.strutLines.Length; strut++) {
        bool active = highlighted.Contains(strut);
        this.strutLines[strut].Stroke = active
          ? highlightBrush : this.baseStrutBrush;
        this.strutLines[strut].StrokeThickness = active ? 6 : 2;
        Panel.SetZIndex(this.strutLines[strut], active ? 1 : 0);
      }
      this.candidateLabel.Text = label;
    }

    private void ShowError(string message) {
      this.statusLabel.Foreground = (Brush)this.FindResource("ErrorBrush");
      this.statusLabel.Text = message ?? "Calibration could not continue.";
    }

    private void ShowSuccess(string message) {
      this.statusLabel.Foreground =
        new SolidColorBrush(WColor.FromRgb(0x16, 0x73, 0x3D));
      this.statusLabel.Text = message;
    }
  }
}

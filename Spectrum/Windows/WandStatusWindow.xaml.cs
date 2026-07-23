using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Spectrum.Base;
// Spectrum declares its own Color type in this namespace, which hides
// System.Windows.Media.Color, so use a distinct name for the WPF color.
using WColor = System.Windows.Media.Color;

namespace Spectrum {

  /**
   * Diagnostic display for the orientation devices (wands, poi, wristbands)
   * that report over UDP into OrientationInput. Polls the input on a timer and
   * lists every device the operator currently considers connected, along with
   * connection-quality stats (update rate, interarrival jitter, packet count,
   * staleness) measured on the receive thread. Also offers a "Calibrate All"
   * control (the same calibration the main UI triggers via
   * config.orientationCalibrate).
   *
   * The window reads thread-safe snapshots (DevicesSnapshot /
   * ConnectionStatsSnapshot) each tick, so it never races the UDP receive
   * thread. Devices drop off the list automatically once OrientationInput times
   * them out.
   */
  public partial class WandStatusWindow : Window {

    // Snapshot poll cadence. OrientationInput times devices out after ~1s, so a
    // few hundred ms keeps the list responsive without busy-polling.
    private static readonly TimeSpan PollInterval =
      TimeSpan.FromMilliseconds(400);

    private readonly Configuration config;
    private readonly OrientationInput orientation;
    private readonly ObservableCollection<WandRow> rows =
      new ObservableCollection<WandRow>();
    private DispatcherTimer? timer;

    public WandStatusWindow(Configuration config, OrientationInput orientation) {
      this.InitializeComponent();
      this.config = config;
      this.orientation = orientation;
      this.wandList.ItemsSource = this.rows;
    }

    private void WindowLoaded(object sender, RoutedEventArgs e) {
      this.timer = new DispatcherTimer { Interval = PollInterval };
      this.timer.Tick += Refresh;
      this.timer.Start();
      this.Refresh(null, null);
    }

    private void WindowClosed(object sender, EventArgs e) {
      if (this.timer != null) {
        this.timer.Stop();
        this.timer.Tick -= Refresh;
        this.timer = null;
      }
    }

    private void Refresh(object? sender, EventArgs? e) {
      var snapshot = this.orientation.DevicesSnapshot();
      var statsSnapshot = this.orientation.ConnectionStatsSnapshot();

      // Reconcile the bound collection with the snapshot in place (keyed by
      // device id, kept sorted) rather than clearing it, so the ListView's
      // selection and scroll position survive each poll.
      foreach (var id in this.rows.Select(r => r.DeviceId).ToList()) {
        if (!snapshot.ContainsKey(id)) {
          this.rows.Remove(this.rows.First(r => r.DeviceId == id));
        }
      }
      foreach (var kvp in snapshot.OrderBy(kvp => kvp.Key)) {
        statsSnapshot.TryGetValue(kvp.Key, out var deviceStats);
        var existing = this.rows.FirstOrDefault(r => r.DeviceId == kvp.Key);
        if (existing == null) {
          int insertAt = this.rows.Count(r => r.DeviceId < kvp.Key);
          this.rows.Insert(
            insertAt, new WandRow(kvp.Key, kvp.Value, deviceStats));
        } else {
          existing.Update(kvp.Value, deviceStats);
        }
      }

      // A wand can be connected (still transmitting) but not moving; only
      // moving wands are visualized, so surface both counts.
      int moving = snapshot.Values.Count(d => d.isMoving);
      this.summaryLabel.Text = this.rows.Count == 0
        ? "No wands connected."
        : this.rows.Count + (this.rows.Count == 1 ? " wand" : " wands") +
          " connected, " + moving + " moving.";
    }

    private void CalibrateClicked(object sender, RoutedEventArgs e) {
      // Picked up by OrientationInput.OperatorUpdate, which calibrates every
      // device and clears the flag.
      this.config.orientationCalibrate = true;
    }

    private void CloseClicked(object sender, RoutedEventArgs e) {
      this.Close();
    }

  }

  // One row in the wand list. Implements INotifyPropertyChanged so values
  // refresh in place without rebuilding the ListView each poll.
  public class WandRow : INotifyPropertyChanged {

    // Quality thresholds (heuristic). A device is timed out by OrientationInput
    // at 1000ms, so staleness well before that signals dropped packets. Jitter
    // bands are in milliseconds of interarrival variation.
    private const double StaleMs = 400.0;
    private const double JitterFairMs = 8.0;
    private const double JitterPoorMs = 20.0;
    // Packet-loss bands (fraction of sent packets missing).
    private const double LossFairFraction = 0.01;
    private const double LossPoorFraction = 0.05;
    // Update-rate bands, as fractions of the wands' 200 Hz transmit cap
    // (OrientationInput.WandMaxTransmitRateHz). A wand that stays connected but
    // whose rate has collapsed well below the ceiling — RF congestion, a dying
    // battery — is degraded even when jitter and loss still read benign, so the
    // rate feeds the rating too.
    private const double RateFairFraction = 0.6;
    private const double RatePoorFraction = 0.3;

    private static readonly Brush GoodBrush = Frozen(0x16, 0x73, 0x3D);
    private static readonly Brush FairBrush = Frozen(0x8A, 0x5A, 0x00);
    private static readonly Brush PoorBrush = Frozen(0xB3, 0x26, 0x1E);

    public event PropertyChangedEventHandler? PropertyChanged;

    public int DeviceId { get; }
    public string TypeName { get; }

    private string action = string.Empty;
    public string Action {
      get => this.action;
      private set => this.Set(ref this.action, value, nameof(this.Action));
    }

    // "Moving" while the device's motion detection considers it in use (and
    // thus visualized); "Still" while it only transmits.
    private string motion = string.Empty;
    public string Motion {
      get => this.motion;
      private set => this.Set(ref this.motion, value, nameof(this.Motion));
    }

    private string orientation = string.Empty;
    public string Orientation {
      get => this.orientation;
      private set =>
        this.Set(ref this.orientation, value, nameof(this.Orientation));
    }

    private string speed = string.Empty;
    public string Speed {
      get => this.speed;
      private set => this.Set(ref this.speed, value, nameof(this.Speed));
    }

    private string rate = string.Empty;
    public string Rate {
      get => this.rate;
      private set => this.Set(ref this.rate, value, nameof(this.Rate));
    }

    private string jitter = string.Empty;
    public string Jitter {
      get => this.jitter;
      private set => this.Set(ref this.jitter, value, nameof(this.Jitter));
    }

    private string loss = string.Empty;
    public string Loss {
      get => this.loss;
      private set => this.Set(ref this.loss, value, nameof(this.Loss));
    }

    private string dataRate = string.Empty;
    public string DataRate {
      get => this.dataRate;
      private set => this.Set(ref this.dataRate, value, nameof(this.DataRate));
    }

    private string packets = string.Empty;
    public string Packets {
      get => this.packets;
      private set => this.Set(ref this.packets, value, nameof(this.Packets));
    }

    private string lastSeen = string.Empty;
    public string LastSeen {
      get => this.lastSeen;
      private set => this.Set(ref this.lastSeen, value, nameof(this.LastSeen));
    }

    private string quality = string.Empty;
    public string Quality {
      get => this.quality;
      private set => this.Set(ref this.quality, value, nameof(this.Quality));
    }

    private Brush qualityBrush = GoodBrush;
    public Brush QualityBrush {
      get => this.qualityBrush;
      private set =>
        this.Set(ref this.qualityBrush, value, nameof(this.QualityBrush));
    }

    // Whether this device is the current orientation spotlight (the single wand
    // whose motion drives the dome). Bound TwoWay to the per-row radio in the VJ
    // HUD's compact wand panel; a user check sets it true, which asks the host to
    // make this device the spotlight via SpotlightRequested. WandStatusWindow
    // never binds this and leaves SpotlightRequested null, so it stays inert
    // there.
    private bool isSpotlight;
    public bool IsSpotlight {
      get => this.isSpotlight;
      set {
        if (this.Set(ref this.isSpotlight, value, nameof(this.IsSpotlight)) &&
            value) {
          this.SpotlightRequested?.Invoke(this.DeviceId);
        }
      }
    }

    // Invoked with this row's DeviceId when IsSpotlight transitions to true from
    // the UI. The host wires this to its spotlight setter; null is a no-op (the
    // read-only diagnostics window).
    public Action<int>? SpotlightRequested;

    public WandRow(
      int deviceId, OrientationDevice device, OrientationDeviceStats stats) {
      this.DeviceId = deviceId;
      this.TypeName = WandTypeNames.Of(device.deviceType);
      this.Update(device, stats);
    }

    public void Update(OrientationDevice device, OrientationDeviceStats stats) {
      this.Action = device.actionFlag == 0 ? "—" : "Button " + device.actionFlag;
      this.Motion = device.isMoving ? "Moving" : "Still";
      var q = device.currentOrientation;
      this.Orientation = string.Format(
        CultureInfo.InvariantCulture,
        "{0,6:F2} {1,6:F2} {2,6:F2} {3,6:F2}",
        q.W, q.X, q.Y, q.Z);
      this.Speed = device.hasSpeed
        ? device.avgDistanceShort.ToString("F3", CultureInfo.InvariantCulture)
        : "—";

      this.Rate = stats.UpdateRateHz > 0
        ? stats.UpdateRateHz.ToString("F1", CultureInfo.InvariantCulture)
        : "—";
      this.Jitter = stats.PacketCount > 1
        ? stats.JitterMs.ToString("F2", CultureInfo.InvariantCulture)
        : "—";
      this.Loss = stats.PacketCount > 1
        ? (stats.PacketLossFraction * 100.0).ToString(
            "F1", CultureInfo.InvariantCulture)
        : "—";
      this.DataRate = stats.DataRateBytesPerSec > 0
        ? (stats.DataRateBytesPerSec / 1000.0).ToString(
            "F2", CultureInfo.InvariantCulture)
        : "—";
      this.Packets = stats.PacketCount.ToString(CultureInfo.InvariantCulture);
      this.LastSeen = stats.MillisSinceLastPacket.ToString(
        "F0", CultureInfo.InvariantCulture);

      this.ApplyQuality(stats);
    }

    // Maps the measured stats onto a coarse Good/Fair/Poor rating plus a matching
    // row color, so the operator can spot a flaky wand at a glance.
    private void ApplyQuality(OrientationDeviceStats stats) {
      if (stats.PacketCount < 2) {
        this.Quality = "…";
        this.QualityBrush = GoodBrush;
        return;
      }
      double rateFraction =
        stats.UpdateRateHz / OrientationInput.WandMaxTransmitRateHz;
      if (stats.MillisSinceLastPacket > StaleMs ||
          stats.JitterMs > JitterPoorMs ||
          stats.PacketLossFraction > LossPoorFraction ||
          rateFraction < RatePoorFraction) {
        this.Quality = "Poor";
        this.QualityBrush = PoorBrush;
      } else if (stats.JitterMs > JitterFairMs ||
                 stats.PacketLossFraction > LossFairFraction ||
                 rateFraction < RateFairFraction) {
        this.Quality = "Fair";
        this.QualityBrush = FairBrush;
      } else {
        this.Quality = "Good";
        this.QualityBrush = GoodBrush;
      }
    }

    private bool Set<T>(ref T field, T value, string name) {
      if (Equals(field, value)) {
        return false;
      }
      field = value;
      this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
      return true;
    }

    private static Brush Frozen(byte r, byte g, byte b) {
      var brush = new SolidColorBrush(WColor.FromRgb(r, g, b));
      brush.Freeze();
      return brush;
    }

  }

}

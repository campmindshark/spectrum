using System.Collections.Generic;
using System.Linq;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * Read model + calibrate action behind the maintenance "Wand status" view —
   * the web port of WandStatusWindow. Merges OrientationInput's device and
   * connection-stats snapshots into flat JSON rows the browser polls, and fires
   * the same "calibrate every device" action the native Calibrate All button
   * triggers (via config.orientationCalibrate).
   *
   * Snapshot() only reads the thread-safe snapshots OrientationInput exposes, so
   * it never races the UDP receive thread. The calibrate write goes through the
   * ControlGateway, so it lands on the same thread a native GUI write would and
   * is picked up by OrientationInput.OperatorUpdate, which clears the flag.
   */
  public sealed class WandStatusController {

    // One device's row. Raw numbers only — the browser formats them and derives
    // the Good/Fair/Poor quality rating, exactly as WandRow does for the native
    // ListView, so the heuristic lives in the view layer either way.
    public sealed class WandStatusRow {
      public int deviceId { get; set; }
      public string typeName { get; set; }
      // 0 = no button held, otherwise the button number.
      public int actionFlag { get; set; }
      // Whether the device's motion detection considers it physically in use;
      // still-but-transmitting devices are excluded from the visualization.
      public bool isMoving { get; set; }
      public double w { get; set; }
      public double x { get; set; }
      public double y { get; set; }
      public double z { get; set; }
      public bool hasSpeed { get; set; }
      public double speed { get; set; }
      public double updateRateHz { get; set; }
      public double jitterMs { get; set; }
      public double packetLossFraction { get; set; }
      public double dataRateBytesPerSec { get; set; }
      public long packetCount { get; set; }
      public double millisSinceLastPacket { get; set; }
    }

    private readonly OrientationInput orientation;
    private readonly ControlGateway gateway;
    private readonly Configuration config;

    public WandStatusController(
      OrientationInput orientation, ControlGateway gateway, Configuration config
    ) {
      this.orientation = orientation;
      this.gateway = gateway;
      this.config = config;
    }

    // Merges the device and stats snapshots into JSON rows, sorted by device id
    // to match the native window's ordering. A device with no stats entry yet
    // still lists, with zeroed connection numbers (default struct).
    public List<WandStatusRow> Snapshot() {
      var devices = this.orientation.DevicesSnapshot();
      var stats = this.orientation.ConnectionStatsSnapshot();
      var rows = new List<WandStatusRow>(devices.Count);
      foreach (var kvp in devices.OrderBy(kvp => kvp.Key)) {
        var device = kvp.Value;
        stats.TryGetValue(kvp.Key, out var s);
        var q = device.currentOrientation;
        rows.Add(new WandStatusRow {
          deviceId = kvp.Key,
          typeName = WandTypeNames.Of(device.deviceType),
          actionFlag = device.actionFlag,
          isMoving = device.isMoving,
          w = q.W, x = q.X, y = q.Y, z = q.Z,
          hasSpeed = device.hasSpeed,
          speed = device.avgDistanceShort,
          updateRateHz = s.UpdateRateHz,
          jitterMs = s.JitterMs,
          packetLossFraction = s.PacketLossFraction,
          dataRateBytesPerSec = s.DataRateBytesPerSec,
          packetCount = s.PacketCount,
          millisSinceLastPacket = s.MillisSinceLastPacket,
        });
      }
      return rows;
    }

    // Fires the global recalibration the native Calibrate All button does:
    // OrientationInput.OperatorUpdate picks up the flag, calibrates every device
    // to its current orientation, and clears it. Marshaled through the gateway
    // so it runs on the operator/UI thread like a native write. Momentary and
    // idempotent, so — like the native button — it takes no advisory lease.
    public void CalibrateAll() {
      this.gateway.Post(() => this.config.orientationCalibrate = true);
    }
  }
}

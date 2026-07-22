using System;
using System.Numerics;

namespace Spectrum {
  public class OrientationDevice {
    // Motion detection. A wand left on the ground keeps transmitting for as
    // long as its switch is on, so "still connected" can't be the test for
    // "should be visualized". Each packet scores the angular speed implied by
    // the orientation change since the previous packet, smoothed with an EWMA
    // so single noisy samples neither wake a resting wand nor stall a moving
    // one. Sustained speed above MOVING_RAD_PER_SEC stamps lastMotionMs, and
    // the device counts as moving until MOTION_TIMEOUT_MS after that stamp, so
    // a brief pause mid-performance doesn't drop it from the dome.
    private const double MOVING_RAD_PER_SEC = 0.05; // ~3°/s; sensor noise and
                                                    // gyro drift sit well below
    private const double MOTION_EWMA_GAIN = 0.1;
    private const long MOTION_TIMEOUT_MS = 3000;

    public int timestamp { get; set; }
    public int deviceType { get; }
    public Quaternion calibrationOrigin { get; set; }
    public Quaternion currentOrientation { get; set; }
    public double avgDistanceShort { get; set; }
    public bool hasSpeed { get; set; }
    public int actionFlag { get; set; }
    // Whether the wand is physically in use (see motion detection above).
    // Recomputed on the receive thread on every packet; devices that stop
    // sending entirely are removed by OrientationInput's timeout instead.
    public bool isMoving { get; set; }

    private long lastMotionMs;
    private double motionSpeedEwma;

    // Smoothed angular velocity of the orientation sensor in radians/second.
    // Exposed on frame snapshots so motion-reactive renderers can scale their
    // response from the physical sensor rather than estimating it per frame.
    public double MotionSpeedRadPerSecond => this.motionSpeedEwma;

    // Device types 1, 3, 4 - wands, wristbands
    public OrientationDevice(int timestamp, int deviceType, Quaternion calibrationOrigin, Quaternion currentOrientation) {
      this.timestamp = timestamp;
      this.deviceType = deviceType;
      this.calibrationOrigin = calibrationOrigin;
      this.currentOrientation = currentOrientation;
      this.hasSpeed = false;
      this.isMoving = true;
      actionFlag = 0;
    }

    // Device type 2 - Adam's poi
    public OrientationDevice(int timestamp, int deviceType, Quaternion calibrationOrigin, Quaternion currentOrientation, double avgDistanceShort) {
      this.timestamp = timestamp;
      this.deviceType = deviceType;
      this.calibrationOrigin = calibrationOrigin;
      this.currentOrientation = currentOrientation;
      this.hasSpeed = true;
      this.avgDistanceShort = avgDistanceShort;
      this.isMoving = true;
      actionFlag = 0;
    }

    public void calibrate() {
      calibrationOrigin = currentOrientation;
    }

    // Scores the orientation change carried by a new packet; called on the
    // receive thread before currentOrientation is overwritten with next.
    // deviceIntervalMs is the gap between the two packets in the device's own
    // clock, so network jitter doesn't distort the speed estimate; a
    // non-positive or huge gap (out-of-order packet, power cycle) skips the
    // scoring rather than producing a bogus speed.
    public void RecordMotion(Quaternion next, int deviceIntervalMs, long nowMs) {
      if (deviceIntervalMs > 0 && deviceIntervalMs <= 1000) {
        // The datagram quaternions are int16-quantized and not exactly unit
        // length, so the dot product must be normalized: acos this close to 1
        // otherwise reads the magnitude error as rotation.
        double denom = currentOrientation.Length() * next.Length();
        if (denom > 1e-6) {
          // |dot| identifies q and -q (the same orientation), so a sign flip
          // in the sensor output doesn't register as a huge jump.
          double dot = Math.Min(
            1.0, Math.Abs(Quaternion.Dot(currentOrientation, next)) / denom);
          double angleRad = 2 * Math.Acos(dot);
          double speed = angleRad * 1000.0 / deviceIntervalMs;
          motionSpeedEwma += (speed - motionSpeedEwma) * MOTION_EWMA_GAIN;
          if (motionSpeedEwma > MOVING_RAD_PER_SEC) {
            lastMotionMs = nowMs;
          }
        }
      }
      RefreshMoving(nowMs);
    }

    // Non-orientation activity — a button press, or the first packet from a
    // new device — also counts as "in use".
    public void NoteActivity(long nowMs) {
      lastMotionMs = nowMs;
      isMoving = true;
    }

    // Re-evaluates the moving flag; called on every packet so the flag decays
    // even when the orientation payload isn't advancing.
    public void RefreshMoving(long nowMs) {
      isMoving = nowMs - lastMotionMs <= MOTION_TIMEOUT_MS;
    }

    // All fields are value types, so a memberwise copy is a fully independent
    // snapshot. Used to hand visualizers a device state they can read off-thread
    // without racing the receive thread's field writes.
    public OrientationDevice Clone() {
      return (OrientationDevice)this.MemberwiseClone();
    }

    public double currentAverageDistance() {
      if (!hasSpeed) {
        return 0f;
      }
      return avgDistanceShort;
    }

    public Quaternion currentRotation() {
      return Quaternion.Multiply(Quaternion.Inverse(currentOrientation), calibrationOrigin);
    }
  }
}

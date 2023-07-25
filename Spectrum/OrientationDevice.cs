using System.Numerics;

namespace Spectrum {
  public class OrientationDevice {
    public int timestamp { get; set; }
    public Quaternion calibrationOrigin { get; set; }
    public Quaternion currentOrientation { get; set; }
    public OrientationDevice(int timestamp, Quaternion calibrationOrigin, Quaternion currentOrientation) {
      this.timestamp = timestamp;
      this.calibrationOrigin = calibrationOrigin;
      this.currentOrientation = currentOrientation;
    }
    public void calibrate() {
      calibrationOrigin = currentOrientation;
    }

    public Quaternion currentRotation() {
      return Quaternion.Multiply(calibrationOrigin, Quaternion.Inverse(currentOrientation));
    }
  }
}

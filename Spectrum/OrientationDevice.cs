using System.Numerics;

namespace Spectrum {
  public class OrientationDevice {
    public int timestamp { get; set; }
    public Quaternion calibrationOrigin { get; set; }
    public Quaternion currentOrientation { get; set; }
    public int actionFlag { get; set; }
    public OrientationDevice(int timestamp, Quaternion calibrationOrigin, Quaternion currentOrientation) {
      this.timestamp = timestamp;
      this.calibrationOrigin = calibrationOrigin;
      this.currentOrientation = currentOrientation;
      actionFlag = 0;
    }
    public void calibrate() {
      calibrationOrigin = currentOrientation;
    }

    public Quaternion currentRotation() {
      return Quaternion.Multiply(calibrationOrigin, Quaternion.Inverse(currentOrientation));
    }
  }
}

﻿using System.Numerics;

namespace Spectrum {
  public class OrientationDevice {
    public int timestamp { get; set; }
    public int deviceType { get; }
    public Quaternion calibrationOrigin { get; set; }
    public Quaternion currentOrientation { get; set; }
    public double avgDistanceShort { get; set; }
    public bool hasSpeed { get; set; }
    public int actionFlag { get; set; }

    // Device types 1, 3, 4 - wands, wristbands
    public OrientationDevice(int timestamp, int deviceType, Quaternion calibrationOrigin, Quaternion currentOrientation) {
      this.timestamp = timestamp;
      this.deviceType = deviceType;
      this.calibrationOrigin = calibrationOrigin;
      this.currentOrientation = currentOrientation;
      this.hasSpeed = false;
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
      actionFlag = 0;
    }

    public void calibrate() {
      calibrationOrigin = currentOrientation;
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

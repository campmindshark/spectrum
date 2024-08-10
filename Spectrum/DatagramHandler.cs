using System;
using System.Numerics;

namespace Spectrum {
  public class DatagramHandler {
    public DatagramHandler() {
    }

    public static (OrientationDevice device, int actionFlag) parseDatagram(byte[] buffer) {
      var timestamp = BitConverter.ToInt32(buffer, 1);
      int deviceType = buffer[5];
      // Device type 1 - original wands
      // Device type 2 - Adam's poi
      // Device type 3 - wands v2
      // Device type 4 - wristband
      // For now, the original wands, wands v2 and wristband all have the same data
      // The poi have an additional rotational speed element
      if (deviceType == 1 || deviceType == 3 || deviceType == 4) {
        short W = BitConverter.ToInt16(buffer, 6);
        short X = BitConverter.ToInt16(buffer, 8);
        short Y = BitConverter.ToInt16(buffer, 10);
        short Z = BitConverter.ToInt16(buffer, 12);
        Quaternion sensorState = new Quaternion(X / 16384.0f, Y / 16384.0f, Z / 16384.0f, W / 16384.0f);
        int actionFlag = buffer[13]; // what the buttons do
        return (device: new OrientationDevice(timestamp, new Quaternion(0, 0, 0, 1), sensorState), actionFlag: actionFlag);
      }
      if (deviceType == 2) {
        short W = BitConverter.ToInt16(buffer, 6);
        short X = BitConverter.ToInt16(buffer, 8);
        short Y = BitConverter.ToInt16(buffer, 10);
        short Z = BitConverter.ToInt16(buffer, 12);
        double rotationalSpeed = BitConverter.ToInt16(buffer, 14) / 65536.0f;
        Quaternion sensorState = new Quaternion(X / 16384.0f, Y / 16384.0f, Z / 16384.0f, W / 16384.0f);
        return (device: new OrientationDevice(timestamp, new Quaternion(0, 0, 0, 1), sensorState, rotationalSpeed), actionFlag: 0);
      }
      return (device: new OrientationDevice(-1, new Quaternion(0, 0, 0, 0), new Quaternion(0, 0, 0, 0)), actionFlag: 0);
    }
  }
}

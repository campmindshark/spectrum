using System;
using System.Numerics;

namespace Spectrum {
  public class DatagramHandler {
    public DatagramHandler() {
    }

    // The deviceId (byte 0), timestamp (bytes 1-4) and deviceType (byte 5) are
    // read before any type-specific unpacking, so every datagram needs at least
    // this many bytes to be inspected at all.
    public const int MinDatagramLength = 6;

    // Smallest datagram length that parseDatagram can unpack without reading
    // past the end of the buffer, given the device type at byte 5. Datagrams
    // shorter than this for their type must be dropped (see OrientationInput).
    public static int RequiredLength(int deviceType) {
      switch (deviceType) {
        // wands / wands v2 / wristband: actionFlag is read at buffer[14].
        case 1:
        case 3:
        case 4:
          return 15;
        // poi: avgDistanceShort is read as a UInt16 at buffer[15..16].
        case 2:
          return 17;
        // Unknown types parse to a placeholder device and read nothing further.
        default:
          return MinDatagramLength;
      }
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
        int actionFlag = buffer[14]; // what the buttons do
        return (device: new OrientationDevice(timestamp, deviceType, new Quaternion(0, 0, 0, 1), sensorState), actionFlag: actionFlag);
      }
      // Device type 2 - Adam's poi
      if (deviceType == 2) {
        short W = BitConverter.ToInt16(buffer, 6);
        short X = BitConverter.ToInt16(buffer, 8);
        short Y = BitConverter.ToInt16(buffer, 10);
        short Z = BitConverter.ToInt16(buffer, 12);
        // Note the poi only have 1 accessable button while in use.
        // This could be used for calibration.
        // I am leaving this here in case I fix the external button on my poi.
        //int actionFlag = buffer[13];

        // avgDistance is the average angular distance traveled in a time period
        double avgDistanceShort = BitConverter.ToUInt16(buffer, 15) / 65536.0;

        Quaternion sensorState = new Quaternion(X / 16384.0f, Y / 16384.0f, Z / 16384.0f, W / 16384.0f);
        return (device: new OrientationDevice(timestamp, deviceType, new Quaternion(0, 0, 0, 1), sensorState, avgDistanceShort), actionFlag: 0);
      }
      return (device: new OrientationDevice(-1, -1, new Quaternion(0, 0, 0, 0), new Quaternion(0, 0, 0, 0)), actionFlag: 0);
    }
  }
}

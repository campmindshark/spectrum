using System;
using System.Numerics;

namespace Spectrum {
  public class DatagramHandler {
    public DatagramHandler() {
    }

    // Byte 0 is always the deviceId, and the two header layouts share an
    // identical prefix through the deviceType byte:
    //
    //   Legacy (device types 1-4): id[1] timestamp[4] deviceType[1] ...
    //   Seq-carrying (device types 5 and 6): id[1] timestamp[4] deviceType[1]
    //     seq[1] ...  -> a uint8 packet sequence number sits AFTER the deviceType,
    //     so timestamp and deviceType keep their legacy offsets and only the
    //     type-specific payload shifts one byte later.
    //
    // Because deviceType is always at byte 5, the layout is decided by the
    // deviceType value alone (no ambiguity, no timestamp aliasing to worry about).
    // Type 5 is the ESP-NOW receiver heartbeat; type 6 is wand v3 (a wand with
    // the seq-carrying header). Both layouts need at least this many bytes before
    // the header can be read (the deviceType is the last of these six).
    public const int MinDatagramLength = 6;

    // Result of reading the common header off a datagram, hiding the two layouts
    // from callers. PayloadOffset is where the type-specific fields begin (6 for
    // the legacy layout, 7 for the seq-carrying layout), so per-type parsing can
    // be written once against that base regardless of the seq byte.
    public readonly struct Header {
      public byte DeviceId { get; }
      public int Timestamp { get; }
      public int DeviceType { get; }
      // The uint8 packet sequence number for the seq-carrying layout (types 5
      // and 6); -1 for the legacy layout, which carries no sequence number.
      public int Sequence { get; }
      public int PayloadOffset { get; }

      public Header(
        byte deviceId, int timestamp, int deviceType, int sequence,
        int payloadOffset) {
        this.DeviceId = deviceId;
        this.Timestamp = timestamp;
        this.DeviceType = deviceType;
        this.Sequence = sequence;
        this.PayloadOffset = payloadOffset;
      }
    }

    // Classifies a datagram's header layout and reads the id/timestamp/deviceType
    // (and, for the seq layout, the sequence number). Returns false when the
    // common header is incomplete, or when a seq-carrying type is missing its
    // sequence byte. An unrecognized type falls through to the legacy
    // interpretation, preserving the pre-existing placeholder-device behavior
    // downstream.
    //
    // With the sequence byte moved AFTER the deviceType, id/timestamp/deviceType
    // occupy the same offsets in both layouts, so a single read of the deviceType
    // at byte 5 decides everything: types {5,6} carry a seq byte at byte 6 and
    // start their payload at byte 7; every other type is legacy with no seq byte
    // and a payload at byte 6.
    public static bool TryReadHeader(byte[] buffer, out Header header) {
      header = default;
      if (buffer.Length < MinDatagramLength) {
        return false;
      }
      byte deviceType = buffer[5];
      int timestamp = BitConverter.ToInt32(buffer, 1);
      if (deviceType == 5 || deviceType == 6) {
        if (buffer.Length < 7) {
          return false;
        }
        header = new Header(
          buffer[0], timestamp, deviceType, buffer[6], 7);
        return true;
      }
      header = new Header(buffer[0], timestamp, deviceType, -1, 6);
      return true;
    }

    // Smallest datagram length that parseDatagram can unpack without reading
    // past the end of the buffer, given the device type. Datagrams shorter than
    // this for their type must be dropped (see OrientationInput). The seq-
    // carrying types (5, 6) already account for the extra sequence byte.
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
        // ESP-NOW receiver heartbeat: id[1] timestamp[4] deviceType[1] seq[1].
        case 5:
          return 7;
        // wand v3: the seq byte after the deviceType shifts the wand payload one
        // byte later, so actionFlag is read at buffer[15] rather than buffer[14].
        case 6:
          return 16;
        // Unknown types parse to a placeholder device and read nothing further.
        default:
          return MinDatagramLength;
      }
    }

    public static (OrientationDevice device, int actionFlag) parseDatagram(byte[] buffer) {
      if (!TryReadHeader(buffer, out var header) ||
          buffer.Length < RequiredLength(header.DeviceType)) {
        return (device: new OrientationDevice(-1, -1, new Quaternion(0, 0, 0, 0), new Quaternion(0, 0, 0, 0)), actionFlag: 0);
      }
      int timestamp = header.Timestamp;
      int deviceType = header.DeviceType;
      // Base offset of the type-specific payload; absorbs the seq byte for v3.
      int p = header.PayloadOffset;
      // Device type 1 - original wands
      // Device type 2 - Adam's poi
      // Device type 3 - wands v2
      // Device type 4 - wristband
      // Device type 6 - wand v3 (same wand payload as 1/3/4, just carried behind
      //   the seq-carrying header)
      // The original wands, wands v2, wristband and wand v3 all share the same
      // quaternion + actionFlag payload. The poi have an additional rotational
      // speed element.
      if (deviceType == 1 || deviceType == 3 || deviceType == 4 || deviceType == 6) {
        short W = BitConverter.ToInt16(buffer, p);
        short X = BitConverter.ToInt16(buffer, p + 2);
        short Y = BitConverter.ToInt16(buffer, p + 4);
        short Z = BitConverter.ToInt16(buffer, p + 6);
        Quaternion sensorState = new Quaternion(X / 16384.0f, Y / 16384.0f, Z / 16384.0f, W / 16384.0f);
        int actionFlag = buffer[p + 8]; // what the buttons do
        return (device: new OrientationDevice(timestamp, deviceType, new Quaternion(0, 0, 0, 1), sensorState), actionFlag: actionFlag);
      }
      // Device type 2 - Adam's poi
      if (deviceType == 2) {
        short W = BitConverter.ToInt16(buffer, p);
        short X = BitConverter.ToInt16(buffer, p + 2);
        short Y = BitConverter.ToInt16(buffer, p + 4);
        short Z = BitConverter.ToInt16(buffer, p + 6);
        // Note the poi only have 1 accessable button while in use.
        // This could be used for calibration.
        // I am leaving this here in case I fix the external button on my poi.
        //int actionFlag = buffer[p + 7];

        // avgDistance is the average angular distance traveled in a time period
        double avgDistanceShort = BitConverter.ToUInt16(buffer, p + 9) / 65536.0;

        Quaternion sensorState = new Quaternion(X / 16384.0f, Y / 16384.0f, Z / 16384.0f, W / 16384.0f);
        return (device: new OrientationDevice(timestamp, deviceType, new Quaternion(0, 0, 0, 1), sensorState, avgDistanceShort), actionFlag: 0);
      }
      return (device: new OrientationDevice(-1, -1, new Quaternion(0, 0, 0, 0), new Quaternion(0, 0, 0, 0)), actionFlag: 0);
    }
  }
}

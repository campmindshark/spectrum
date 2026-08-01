using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Numerics;
using static Spectrum.LayerPipeline.Tests.LayerPipelineTestFixtures;

namespace Spectrum.LayerPipeline.Tests {

  [TestClass]
  [DoNotParallelize]
  public sealed class WandProtocolTests {


    [TestMethod]
    public void CrcFixture() {
      byte[] check = System.Text.Encoding.ASCII.GetBytes("123456789");
      Assert.IsTrue(Crc8.Compute(check) == 0xF4,
        "CRC-8/SMBus check vector changed");
    }

    [TestMethod]
    public void FrameFixture() {
      // Delimiter-stripped COBS frame for a type-6 wand packet. The decoded
      // payload ends in the CRC byte and contains several zero-valued fields.
      byte[] encoded = {
        0x03, 0x09, 0x2A, 0x01, 0x01, 0x03, 0x06, 0xFE, 0x02,
        0x40, 0x02, 0x20, 0x01, 0x01, 0x04, 0xF0, 0x02, 0xF4,
      };
      byte[] expectedDecoded = {
        0x09, 0x2A, 0x00, 0x00, 0x00, 0x06, 0xFE, 0x00,
        0x40, 0x00, 0x20, 0x00, 0x00, 0x00, 0xF0, 0x02, 0xF4,
      };

      Assert.IsTrue(CobsCodec.TryDecode(encoded, out byte[]? decoded),
        "valid receiver frame was rejected");
      AssertBytes(expectedDecoded, decoded, "decoded receiver frame");

      byte[] payload = decoded[..^1];
      Assert.IsTrue(Crc8.Compute(payload) == decoded[^1],
        "receiver frame CRC was not preserved");
      Assert.IsTrue(DatagramHandler.TryReadHeader(payload, out var header),
        "type-6 header was rejected");
      Assert.IsTrue(header.DeviceId == 9 && header.Timestamp == 42,
        "common header fields shifted");
      Assert.IsTrue(header.DeviceType == 6 && header.Sequence == 254 &&
        header.PayloadOffset == 7,
        "sequence-carrying header was misclassified");

      var parsed = DatagramHandler.parseDatagram(payload);
      Assert.IsTrue(parsed.device.timestamp == 42 && parsed.device.deviceType == 6,
        "type-6 identity fields changed");
      Assert.IsTrue(parsed.actionFlag == 2, "type-6 action byte shifted");
      AssertQuaternion(
        new Quaternion(0.5f, 0, -0.25f, 1),
        parsed.device.currentOrientation,
        "type-6 orientation");
    }

    [TestMethod]
    public void LegacyFixture() {
      byte[] payload = {
        0x2A, 0x78, 0x56, 0x34, 0x12, 0x03, 0x00, 0x20,
        0x00, 0xC0, 0x00, 0x10, 0x00, 0xE0, 0x04,
      };

      Assert.IsTrue(DatagramHandler.TryReadHeader(payload, out var header),
        "legacy header was rejected");
      Assert.IsTrue(header.DeviceId == 0x2A && header.Timestamp == 0x12345678,
        "legacy common header fields shifted");
      Assert.IsTrue(header.DeviceType == 3 && header.Sequence == -1 &&
        header.PayloadOffset == 6,
        "legacy header was misclassified");

      var parsed = DatagramHandler.parseDatagram(payload);
      Assert.IsTrue(parsed.actionFlag == 4, "legacy action byte shifted");
      AssertQuaternion(
        new Quaternion(-1, 0.25f, -0.5f, 0.5f),
        parsed.device.currentOrientation,
        "legacy orientation");
    }

    [TestMethod]
    public void TruncatedFramesFailClosed() {
      Assert.IsTrue(!CobsCodec.TryDecode(
          new byte[] { 0x08, 1, 2, 3, 4, 5, 6 }, out _),
        "COBS code that overruns its frame was accepted");
      Assert.IsTrue(!CobsCodec.TryDecode(
          new byte[] { 0x07, 1, 2, 3, 4, 5, 6 }, out _),
        "decoded frame shorter than a heartbeat was accepted");

      byte[] missingSequence = { 1, 0, 0, 0, 0, 6 };
      Assert.IsTrue(!DatagramHandler.TryReadHeader(missingSequence, out _),
        "type-6 header without its sequence byte was accepted");

      byte[] headerOnly = { 1, 0, 0, 0, 0, 6, 1 };
      var parsed = DatagramHandler.parseDatagram(headerOnly);
      Assert.IsTrue(parsed.device.timestamp == -1 && parsed.device.deviceType == -1,
        "truncated type-6 payload produced a device");
    }

    [TestMethod]
    public void MotionDetection() {
      var device = new OrientationDevice(
        0, 6, Quaternion.Identity, Quaternion.Identity);
      device.RefreshMoving(4000);
      Assert.IsTrue(!device.isMoving, "inactive wand did not become idle");

      device.RecordMotion(-Quaternion.Identity, 5, 4000);
      Assert.IsTrue(!device.isMoving,
        "equivalent quaternion sign flip counted as movement");

      Quaternion quarterTurn = Quaternion.CreateFromAxisAngle(
        Vector3.UnitZ, MathF.PI / 2);
      device.RecordMotion(quarterTurn, 1001, 5000);
      Assert.IsTrue(!device.isMoving,
        "implausible packet interval counted as movement");

      device.RecordMotion(quarterTurn, 100, 5000);
      Assert.IsTrue(device.isMoving, "deliberate rotation did not wake the wand");
      Assert.IsTrue(device.MotionSpeedRadPerSecond > 0,
        "orientation angular speed was not exposed to renderers");
      device.RefreshMoving(8000);
      Assert.IsTrue(device.isMoving, "wand became idle inside the pause grace period");
      device.RefreshMoving(8001);
      Assert.IsTrue(!device.isMoving, "wand remained active after the pause grace period");
    }

    private static void AssertQuaternion(
      Quaternion expected, Quaternion actual, string name
    ) {
      AssertClose(expected.X, actual.X, name + " X");
      AssertClose(expected.Y, actual.Y, name + " Y");
      AssertClose(expected.Z, actual.Z, name + " Z");
      AssertClose(expected.W, actual.W, name + " W");
    }

    private static void AssertBytes(
      byte[] expected, byte[] actual, string name
    ) {
      Assert.IsTrue(expected.Length == actual.Length,
        name + " length changed");
      for (int i = 0; i < expected.Length; i++) {
        Assert.IsTrue(expected[i] == actual[i],
          name + " differs at byte " + i);
      }
    }

  }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;

namespace Spectrum.LayerPipeline.Tests {

  // These tests terminate the real TCP client on a loopback socket and assert
  // the bytes received by an OPC server. The full-dome fixture was derived
  // independently from the deployed source revision by
  // Fixtures/Get-KnownGoodOpcBaseline.ps1.
  internal static class OPCWireTests {
    private const string KnownGoodCommit =
      "72d68765ae2465724ed1958c8fa5f1709b95000b";
    private const string KnownGoodDomeSha256 =
      "2d56011c81d89c182884d7a8d1fe869a81ff5a9b0e489c917eb8a1f0606e715f";
    private const string KnownGoodStrutPatternSha256 =
      "fc385d8219be2736a2b6e85fb6f58289be28aaadbb743c67556d147a44065f46";
    private const int KnownGoodLogicalPixels = 7580;
    private const int KnownGoodWirePixels = 8500;
    private const int StrutPatternFrames = 191;
    private const double DeployedDomeBrightness = 0.356915762888129;
    private const int KnownGoodMaxStripLength = 214;
    private const int StrandsPerCable = 4;
    private const int CablePixels =
      KnownGoodMaxStripLength * StrandsPerCable;
    private const int ControllerPixels =
      KnownGoodMaxStripLength * 8 * 5;

    public static void Register(Action<string, Action> run) {
      run("OPC bytes use the deployed header and RGB layout", HeaderAndRgb);
      run("OPC flush snapshots and persists dense pixels", FlushSemantics);
      run("OPC emits one valid message per populated channel", MultiChannel);
      run("threaded OPC path sends the same wire bytes", ThreadedOutput);
      run("OPC rejects payloads that overflow its wire header", PayloadLimit);
      run("dome OPC frame matches deployed wire fixture", GoldenDomeFrame);
      run("Iterate Through Struts matches deployed OPC sequence",
        IterateThroughStruts);
      run("dome cable calibration permutes OPC cable blocks", CableMapping);
    }

    private static void HeaderAndRgb() {
      using var sink = new LoopbackSink();
      OPCAPI opc = ConnectOpc(sink, 7);
      try {
        opc.SetPixel(0, 0x123456);
        opc.SetPixel(2, 0xABCDEF);
        opc.Flush();
        Send(opc.OperatorUpdate);

        AssertBytes(
          new byte[] {
            7, 0, 0, 9,
            0x12, 0x34, 0x56,
            0x00, 0x00, 0x00,
            0xAB, 0xCD, 0xEF,
          },
          sink.ReceiveMessage(),
          "single-channel OPC frame"
        );
      } finally {
        sink.CloseConnection();
        opc.Active = false;
      }
    }

    private static void FlushSemantics() {
      using var sink = new LoopbackSink();
      OPCAPI opc = ConnectOpc(sink, 3);
      try {
        opc.SetPixel(0, 0x102030);
        opc.SetPixel(2, 0x708090);
        opc.Flush();

        // A write after Flush belongs to the next frame and must not mutate the
        // already-realized frame waiting to be sent.
        opc.SetPixel(0, 0xA0B0C0);
        Send(opc.OperatorUpdate);
        AssertBytes(
          Message(3, 0x102030, 0x000000, 0x708090),
          sink.ReceiveMessage(),
          "realized frame"
        );

        // Pixel 2 is not rewritten, so it persists exactly as it did in the
        // dictionary implementation at the deployed revision.
        opc.Flush();
        Send(opc.OperatorUpdate);
        AssertBytes(
          Message(3, 0xA0B0C0, 0x000000, 0x708090),
          sink.ReceiveMessage(),
          "next persistent frame"
        );
      } finally {
        sink.CloseConnection();
        opc.Active = false;
      }
    }

    private static void MultiChannel() {
      using var sink = new LoopbackSink();
      OPCAPI opc = ConnectOpc(sink, null);
      try {
        opc.SetPixel(9, 1, 0x112233);
        opc.SetPixel(2, 0, 0x445566);
        opc.Flush();
        Send(opc.OperatorUpdate);

        var messages = new Dictionary<byte, byte[]>();
        for (int i = 0; i < 2; i++) {
          byte[] message = sink.ReceiveMessage();
          Assert(message[1] == 0, "OPC command was not Set Pixel Colors");
          Assert(messages.TryAdd(message[0], message),
            "duplicate OPC channel " + message[0]);
        }
        Assert(messages.Count == 2, "wrong OPC channel count");
        AssertBytes(Message(9, 0x000000, 0x112233), messages[9],
          "channel 9");
        AssertBytes(Message(2, 0x445566), messages[2], "channel 2");
      } finally {
        sink.CloseConnection();
        opc.Active = false;
      }
    }

    private static void PayloadLimit() {
      using var sink = new LoopbackSink();
      OPCAPI opc = ConnectOpc(sink, 0);
      try {
        opc.SetPixel(OPCAPI.MaxPixelsPerChannel - 1, 0x010203);
        AssertThrows<ArgumentOutOfRangeException>(
          () => opc.SetPixel(-1, 0), "negative pixel index");
        AssertThrows<ArgumentOutOfRangeException>(
          () => opc.SetPixel(OPCAPI.MaxPixelsPerChannel, 0),
          "oversized OPC payload");
      } finally {
        sink.CloseConnection();
        opc.Active = false;
      }
    }

    private static void ThreadedOutput() {
      using var sink = new LoopbackSink();
      OPCAPI opc = ConnectOpc(sink, 5, true);
      try {
        opc.SetPixel(0, 0xC0FFEE);
        opc.Flush();
        AssertBytes(Message(5, 0xC0FFEE), sink.ReceiveMessage(),
          "threaded OPC frame");
      } finally {
        sink.CloseConnection();
        opc.Active = false;
      }
    }

    private static void GoldenDomeFrame() {
      using var sink = new LoopbackSink();
      global::Spectrum.SpectrumConfiguration config;
      LEDDomeOutput output = ConnectDome(sink, null, out config);
      try {
        DomeFrame frame = GoldenFrame(output);
        byte[] message = Capture(output, sink, frame);
        SaveCapture("current-identity.opc", message);
        Assert(message[0] == 0, "dome OPC channel changed");
        Assert(message[1] == 0, "dome OPC command changed");
        Assert(PayloadLength(message) == KnownGoodWirePixels * 3,
          "dome OPC payload is not the deployed dense length");
        Assert(message.Length == KnownGoodWirePixels * 3 + 4,
          "dome OPC TCP frame length changed");

        int nonBlack = 0;
        for (int i = 4; i < message.Length; i += 3) {
          if ((message[i] | message[i + 1] | message[i + 2]) != 0) {
            nonBlack++;
          }
        }
        Assert(nonBlack == KnownGoodLogicalPixels,
          "wire frame lost pixels or stopped zero-filling strip gaps");

        string actual = Convert.ToHexString(
          SHA256.HashData(message)).ToLowerInvariant();
        Assert(actual == KnownGoodDomeSha256,
          "wire fixture differs from known-good commit " + KnownGoodCommit +
          ": expected " + KnownGoodDomeSha256 + ", got " + actual);
      } finally {
        sink.CloseConnection();
        output.Active = false;
      }
    }

    private static void CableMapping() {
      using var sink = new LoopbackSink();
      global::Spectrum.SpectrumConfiguration config;
      LEDDomeOutput output = ConnectDome(
        sink, Enumerable.Range(0, LEDDomeOutput.NumCables).ToArray(),
        out config);
      try {
        DomeFrame frame = GoldenFrame(output);
        byte[] identityMessage = Capture(output, sink, frame);
        SaveCapture("current-cable-identity.opc", identityMessage);
        int[] identity = PayloadPixels(identityMessage);

        // controller -> endpoint. Swap the first and last physical cables and
        // leave the other eight alone.
        int[] routing = Enumerable.Range(
          0, LEDDomeOutput.NumCables).ToArray();
        routing[0] = 9;
        routing[9] = 0;
        config.domeCableMapping = routing;
        byte[] mappedMessage = Capture(output, sink, frame);
        SaveCapture("current-cable-swapped.opc", mappedMessage);
        int[] mapped = PayloadPixels(mappedMessage);

        for (int controller = 0;
            controller < LEDDomeOutput.NumCables; controller++) {
          int endpoint = routing[controller];
          for (int offset = 0; offset < CablePixels; offset++) {
            int expected = identity[endpoint * CablePixels + offset];
            int actual = mapped[controller * CablePixels + offset];
            Assert(actual == expected,
              "cable mapping mismatch at controller " + controller +
              ", cable pixel " + offset);
          }
        }
      } finally {
        sink.CloseConnection();
        output.Active = false;
      }
    }

    private static void IterateThroughStruts() {
      using var sink = new LoopbackSink();
      global::Spectrum.SpectrumConfiguration config;
      LEDDomeOutput output = ConnectDome(sink, null, out config);
      config.domeTestPattern = 2;
      config.domeMaxBrightness = 1;
      config.domeBrightness = DeployedDomeBrightness;
      var pattern =
        new global::Spectrum.LEDDomeStrutIterationDiagnosticVisualizer(
          config, output);
      pattern.Enabled = true;
      using IncrementalHash sequence = IncrementalHash.CreateHash(
        HashAlgorithmName.SHA256);
      using FileStream capture = OpenCapture(
        "current-iterate-through-struts.opc");
      try {
        for (int frame = 0; frame < StrutPatternFrames; frame++) {
          pattern.AdvancePattern();
          Send(output.OperatorUpdate);
          byte[] message = sink.ReceiveMessage();
          Assert(message[0] == 0 && message[1] == 0,
            "strut pattern frame " + frame + " has the wrong OPC header");
          Assert(message.Length == KnownGoodWirePixels * 3 + 4,
            "strut pattern frame " + frame + " has the wrong wire length");
          sequence.AppendData(message);
          capture?.Write(message, 0, message.Length);
        }
        string actual = Convert.ToHexString(
          sequence.GetHashAndReset()).ToLowerInvariant();
        Assert(actual == KnownGoodStrutPatternSha256,
          "Iterate Through Struts differs from known-good commit " +
          KnownGoodCommit + " across " + StrutPatternFrames +
          " frames: expected " + KnownGoodStrutPatternSha256 +
          ", got " + actual);
      } finally {
        sink.CloseConnection();
        output.Active = false;
      }
    }

    private static OPCAPI ConnectOpc(
      LoopbackSink sink, byte? defaultChannel, bool separateThread = false
    ) {
      string address = "127.0.0.1:" + sink.Port;
      if (defaultChannel.HasValue) {
        address += ":" + defaultChannel.Value;
      }
      var opc = new OPCAPI(address, separateThread, _ => { });
      opc.Active = true;
      sink.Accept();
      return opc;
    }

    private static LEDDomeOutput ConnectDome(
      LoopbackSink sink, int[] cableMapping,
      out global::Spectrum.SpectrumConfiguration config
    ) {
      config = new global::Spectrum.SpectrumConfiguration {
        domeEnabled = true,
        domeOutputInSeparateThread = false,
        domeBeagleboneOPCAddress = "127.0.0.1:" + sink.Port,
        domeCableMapping = cableMapping,
      };
      var output = new LEDDomeOutput(
        config, new RuntimeTelemetry(), new BeatBroadcaster(config));
      output.Active = true;
      sink.Accept();
      return output;
    }

    private static DomeFrame GoldenFrame(LEDDomeOutput output) {
      DomeFrame frame = output.MakeDomeFrame();
      Assert(frame.pixels.Length == KnownGoodLogicalPixels,
        "logical dome pixel count differs from deployed topology");
      for (int i = 0; i < frame.pixels.Length; i++) {
        frame.pixels[i].color = GoldenColor(i);
      }
      return frame;
    }

    private static int GoldenColor(int logicalPixel) {
      int r = (logicalPixel * 73 + 19) & 0xFF;
      int g = (logicalPixel * 151 + 43) & 0xFF;
      int b = (logicalPixel * 199 + 71) & 0xFF;
      return (r << 16) | (g << 8) | b;
    }

    private static byte[] Capture(
      LEDDomeOutput output, LoopbackSink sink, DomeFrame frame
    ) {
      output.WriteBuffer(frame);
      output.Flush();
      Send(output.OperatorUpdate);
      return sink.ReceiveMessage();
    }

    private static void Send(Action update) {
      // OPC output is intentionally capped at 200 Hz. Wait past its 5 ms
      // interval so these tests exercise the real send path deterministically.
      Thread.Sleep(8);
      update();
    }

    private static byte[] Message(byte channel, params int[] colors) {
      int payloadLength = colors.Length * 3;
      var bytes = new byte[payloadLength + 4];
      bytes[0] = channel;
      bytes[1] = 0;
      bytes[2] = (byte)(payloadLength >> 8);
      bytes[3] = (byte)payloadLength;
      int offset = 4;
      foreach (int color in colors) {
        bytes[offset++] = (byte)(color >> 16);
        bytes[offset++] = (byte)(color >> 8);
        bytes[offset++] = (byte)color;
      }
      return bytes;
    }

    private static int[] PayloadPixels(byte[] message) {
      Assert(message[1] == 0, "unexpected OPC command");
      int payloadLength = PayloadLength(message);
      Assert(payloadLength % 3 == 0, "OPC RGB payload is not pixel-aligned");
      Assert(payloadLength + 4 == message.Length,
        "OPC header length does not match TCP bytes");
      Assert(payloadLength / 3 <= ControllerPixels,
        "OPC dome payload exceeds controller capacity");
      var pixels = new int[ControllerPixels];
      for (int pixel = 0; pixel < payloadLength / 3; pixel++) {
        int offset = 4 + pixel * 3;
        pixels[pixel] =
          (message[offset] << 16) |
          (message[offset + 1] << 8) |
          message[offset + 2];
      }
      return pixels;
    }

    private static int PayloadLength(byte[] message) =>
      (message[2] << 8) | message[3];

    private static void SaveCapture(string name, byte[] message) {
      using FileStream capture = OpenCapture(name);
      capture?.Write(message, 0, message.Length);
    }

    private static FileStream OpenCapture(string name) {
      string directory = Environment.GetEnvironmentVariable(
        "SPECTRUM_OPC_CAPTURE_DIR");
      if (string.IsNullOrWhiteSpace(directory)) {
        return null;
      }
      Directory.CreateDirectory(directory);
      return File.Open(
        Path.Combine(directory, name), FileMode.Create,
        FileAccess.Write, FileShare.None);
    }

    private static void AssertBytes(
      byte[] expected, byte[] actual, string name
    ) {
      Assert(expected.Length == actual.Length,
        name + " expected " + expected.Length + " bytes, got " +
        actual.Length);
      for (int i = 0; i < expected.Length; i++) {
        Assert(expected[i] == actual[i],
          name + " differs at byte " + i + ": expected 0x" +
          expected[i].ToString("X2") + ", got 0x" +
          actual[i].ToString("X2"));
      }
    }

    private static void AssertThrows<T>(Action action, string name)
        where T : Exception {
      try {
        action();
      } catch (T) {
        return;
      }
      throw new InvalidOperationException(
        name + " did not throw " + typeof(T).Name);
    }

    private static void Assert(bool condition, string message) {
      if (!condition) {
        throw new InvalidOperationException(message);
      }
    }

    private sealed class LoopbackSink : IDisposable {
      private readonly TcpListener listener;
      private readonly Task<Socket> accepting;
      private Socket socket;

      public int Port { get; }

      public LoopbackSink() {
        this.listener = new TcpListener(IPAddress.Loopback, 0);
        this.listener.Start();
        this.Port = ((IPEndPoint)this.listener.LocalEndpoint).Port;
        this.accepting = this.listener.AcceptSocketAsync();
      }

      public void Accept() {
        this.socket = this.accepting.GetAwaiter().GetResult();
        this.socket.ReceiveTimeout = 2000;
      }

      public byte[] ReceiveMessage() {
        Assert(this.socket != null, "loopback OPC client was not accepted");
        byte[] header = this.ReceiveExactly(4);
        int payloadLength = (header[2] << 8) | header[3];
        byte[] payload = this.ReceiveExactly(payloadLength);
        var message = new byte[header.Length + payload.Length];
        Buffer.BlockCopy(header, 0, message, 0, header.Length);
        Buffer.BlockCopy(payload, 0, message, header.Length, payload.Length);
        return message;
      }

      private byte[] ReceiveExactly(int count) {
        var bytes = new byte[count];
        int received = 0;
        while (received < count) {
          int read = this.socket.Receive(
            bytes, received, count - received, SocketFlags.None);
          if (read == 0) {
            throw new InvalidOperationException(
              "OPC client closed after " + received + " of " + count +
              " expected bytes");
          }
          received += read;
        }
        return bytes;
      }

      public void Dispose() {
        this.CloseConnection();
        this.listener.Stop();
      }

      public void CloseConnection() {
        this.socket?.Dispose();
        this.socket = null;
      }
    }
  }
}

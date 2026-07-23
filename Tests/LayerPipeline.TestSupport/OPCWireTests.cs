using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Spectrum.Base;
using Spectrum.LEDs;

namespace Spectrum.LayerPipeline.Tests {

  // These tests terminate the real TCP client on a loopback socket and assert
  // the bytes received by an OPC server. The full-dome fixture was derived
  // independently from the deployed source revision by
  // Fixtures/Get-KnownGoodOpcBaseline.ps1.
  public static class OPCWireTests {
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
    private const int PortPixels = KnownGoodMaxStripLength;
    private const int PortsPerController = LEDDomeOutput.NumPortsPerBox;
    private const int ControllerPixels =
      PortPixels * PortsPerController * 5;

    public static void Register(Action<string, Action> run) {
      run("OPC bytes use the deployed header and RGB layout", HeaderAndRgb);
      run("OPC flush snapshots and persists dense pixels", FlushSemantics);
      run("OPC emits one valid message per populated channel", MultiChannel);
      run("threaded OPC path sends the same wire bytes", ThreadedOutput);
      run("OPC rejects payloads that overflow its wire header", PayloadLimit);
      run("dome gradients reject invalid palette ranges",
        GradientRangeValidation);
      run("dome OPC frame matches deployed wire fixture", GoldenDomeFrame);
      run("Iterate Through Struts matches deployed OPC sequence",
        IterateThroughStruts);
      run("dome cable calibration permutes OPC cable blocks", CableMapping);
      run("dome port permutation composes with cable mapping", PortMapping);
      run("dome default config exposes identity port mapping",
        DefaultPortMappingConfig);
      run("default config has neutral hardware selections and no retired fields",
        SanitizedDefaultConfig);
      run("dome port mappings round-trip without aliasing",
        PortMappingConfigurationContract);
      run("two-stage dome calibration owns and commits one atomic draft",
        TwoStageCalibrationContract);
      run("dome calibration separates raw hardware and simulator frames",
        CalibrationDiagnosticFrames);
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

    private static void GradientRangeValidation() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var output = new LEDDomeOutput(
        config, new RuntimeTelemetry(), new BeatBroadcaster(config));

      AssertThrows<ArgumentException>(
        () => output.GetGradientBetweenColors(3, 3, .5, 0, false),
        "equal gradient endpoints");
      AssertThrows<ArgumentException>(
        () => output.GetGradientBetweenColors(4, 3, .5, 0, false),
        "reversed gradient endpoints");
      AssertThrows<ArgumentOutOfRangeException>(
        () => output.GetGradientBetweenColors(-1, 3, .5, 0, false),
        "negative gradient endpoint");
      AssertThrows<ArgumentOutOfRangeException>(
        () => output.GetGradientBetweenColors(3, 7, .5, 0, false, -1),
        "negative palette index");
      AssertThrows<ArgumentException>(
        () => output.GetGradientBetweenColors(3, 7, double.NaN, 0, false),
        "non-finite gradient position");
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
        config.ReplaceDomeCableMapping(routing);
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

    private static void PortMapping() {
      using var sink = new LoopbackSink();
      global::Spectrum.SpectrumConfiguration config;
      int[] identityCables = Enumerable.Range(
        0, LEDDomeOutput.NumCables).ToArray();
      LEDDomeOutput output = ConnectDome(sink, identityCables, out config);
      try {
        DomeFrame frame = GoldenFrame(output);
        int[] identity = PayloadPixels(Capture(output, sink, frame));

        // Cable entries are controller cable -> physical endpoint; port entries
        // are physical port -> legacy path. Rotate the cable endpoints and use
        // an arbitrary cross-half port permutation so the assertion covers
        // their composition, not just adjacent swaps.
        int[] cableMapping = Enumerable.Range(
          0, LEDDomeOutput.NumCables).Select(
            cable => (cable + 3) % LEDDomeOutput.NumCables).ToArray();
        int[][] portMappings = {
          new[] { 7, 0, 5, 2, 6, 1, 3, 4 },
          new[] { 4, 3, 2, 1, 0, 7, 6, 5 },
          new[] { 2, 3, 4, 5, 6, 7, 0, 1 },
          new[] { 1, 0, 3, 2, 5, 4, 7, 6 },
          new[] { 6, 5, 4, 7, 2, 1, 0, 3 },
        };
        config.ReplaceDomeCableMapping(cableMapping);
        config.ReplaceDomePortMappings(PortMappingDtos(portMappings));
        int[] mapped = PayloadPixels(Capture(output, sink, frame));

        for (int controllerCable = 0;
            controllerCable < LEDDomeOutput.NumCables; controllerCable++) {
          int physicalEndpoint = cableMapping[controllerCable];
          int physicalBox = physicalEndpoint / 2;
          int physicalHalf = physicalEndpoint % 2;
          int controllerBox = controllerCable / 2;
          int controllerHalf = controllerCable % 2;
          for (int portWithinCable = 0;
              portWithinCable < StrandsPerCable; portWithinCable++) {
            int controllerPort =
              controllerHalf * StrandsPerCable + portWithinCable;
            int physicalPort =
              physicalHalf * StrandsPerCable + portWithinCable;
            int legacyPath = portMappings[physicalBox][physicalPort];
            int expectedStart =
              (physicalBox * PortsPerController + legacyPath) * PortPixels;
            int actualStart =
              (controllerBox * PortsPerController + controllerPort) * PortPixels;
            for (int offset = 0; offset < PortPixels; offset++) {
              Assert(mapped[actualStart + offset] == identity[expectedStart + offset],
                "port mapping mismatch at controller box " + controllerBox +
                ", port " + controllerPort + ", pixel " + offset);
            }
          }
        }

        // A bad setting fails closed only for its own box. Other boxes retain
        // their independent mappings, and the live replacement rebuilds the
        // cached wire map.
        config.ReplaceDomeCableMapping(identityCables);
        portMappings[2] = new[] { 0, 0, 2, 3, 4, 5, 6, 7 };
        config.ReplaceDomePortMappings(PortMappingDtos(portMappings));
        int[] containedFallback = PayloadPixels(Capture(output, sink, frame));
        for (int box = 0; box < LEDDomeOutput.NumDomeBoxes; box++) {
          int[] expectedMapping = box == 2
            ? Enumerable.Range(0, PortsPerController).ToArray()
            : portMappings[box];
          AssertBoxPortMapping(
            containedFallback, identity, box, expectedMapping,
            "invalid per-box fallback");
        }

        // A missing five-box value falls back directly to identity for every
        // box; there is no older shared setting to consult.
        config.ReplaceDomePortMappings(null);
        int[] missingFallback = PayloadPixels(Capture(output, sink, frame));
        for (int box = 0; box < LEDDomeOutput.NumDomeBoxes; box++) {
          AssertBoxPortMapping(
            missingFallback, identity, box,
            Enumerable.Range(0, PortsPerController).ToArray(),
            "missing per-box identity fallback");
        }
      } finally {
        sink.CloseConnection();
        output.Active = false;
      }
    }

    private static void DefaultPortMappingConfig() {
      string path = Path.Combine(
        AppContext.BaseDirectory, "spectrum_default_config.xml");
      using FileStream stream = File.OpenRead(path);
      var config =
        new XSerializer.XmlSerializer<global::Spectrum.SpectrumConfigurationDocument>(
        ).Deserialize(stream).ToConfiguration();
      Assert(config.domePortMappings.Length == LEDDomeOutput.NumDomeBoxes &&
          config.domePortMappings.All(mapping =>
            mapping.SequenceEqual(
              Enumerable.Range(0, PortsPerController))),
        "default config per-box mappings are missing or not identity");
    }

    private static void SanitizedDefaultConfig() {
      string path = Path.Combine(
        AppContext.BaseDirectory, "spectrum_default_config.xml");
      XDocument document = XDocument.Load(path);
      XElement root = document.Root ??
        throw new Exception("default config has no root element");
      var retiredElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "audioDeviceIndex",
        "huesEnabled",
        "huesOutputInSeparateThread",
        "hueDelay",
        "hueIdleOnSilent",
        "hueURL",
        "hueIndices",
        "hueOverrideIsCustom",
        "hueOverrideIndex",
        "lightsOff",
        "redAlert",
        "controlLights",
        "brighten",
        "colorslide",
        "sat",
        "peakC",
        "dropQ",
        "dropT",
        "kickQ",
        "kickT",
        "snareQ",
        "snareT",
        "ledBoardEnabled",
        "ledBoardOutputInSeparateThread",
        "boardBeagleboneOPCAddress",
        "boardRowLength",
        "boardRowsPerStrip",
        "boardBrightness",
        "colorPaletteIndex",
        "colorPalette",
        "paletteModelVersion",
        "midiInputInSeparateThread",
        "levelDriverPresets",
        "channelToAudioLevelDriverPreset",
        "channelToMidiLevelDriverPreset",
      };
      string[] foundRetired = root.Elements()
        .Select(element => element.Name.LocalName)
        .Where(retiredElements.Contains)
        .ToArray();
      Assert(foundRetired.Length == 0,
        "default config contains retired fields: " +
        string.Join(", ", foundRetired));

      using FileStream stream = File.OpenRead(path);
      var config =
        new XSerializer.XmlSerializer<global::Spectrum.SpectrumConfigurationDocument>(
        ).Deserialize(stream).ToConfiguration();
      Assert(string.IsNullOrWhiteSpace(config.audioDeviceID),
        "default config selects an audio device");
      Assert(config.midiDevices != null && config.midiDevices.Count == 0,
        "default config selects a MIDI device");
      Assert(config.domePalettes.Length == 8,
        "default config does not use the named live palette model");
      BeatSettingsSnapshot beat =
        ((IRuntimeSettingsConfiguration)config).BeatSettingsSnapshot;
      Assert(beat.TryGetMidiPreset(
          0, out MidiLevelDriverSettingsSnapshot envelope) &&
          envelope.AttackTime == 10 && envelope.PeakLevel == 1 &&
          envelope.DecayTime == 20 && envelope.SustainLevel == 0.8 &&
          envelope.ReleaseTime == 10,
        "default config does not define its MIDI level-driver channel");
    }

    private static void PortMappingConfigurationContract() {
      int[] cableMapping = Enumerable.Range(
        0, LEDDomeOutput.NumCables).Reverse().ToArray();
      int[][] values = Enumerable.Range(0, LEDDomeOutput.NumDomeBoxes)
        .Select(box => Enumerable.Range(0, PortsPerController)
          .Select(port => (port + box) % PortsPerController).ToArray())
        .ToArray();
      DomePortMapping[] assignedMappings = PortMappingDtos(values);
      var config = new global::Spectrum.SpectrumConfiguration();
      config.ReplaceDomeCableMapping(cableMapping);
      config.ReplaceDomePortMappings(assignedMappings);

      cableMapping[0] = 0;
      assignedMappings[1].ports![0] = 7;
      Assert(config.domeCableMapping[0] == LEDDomeOutput.NumCables - 1 &&
          config.domePortMappings[1][0] == 1,
        "configuration retained an alias to assigned mapping values");

      Assert(config.domeCableMapping[0] == LEDDomeOutput.NumCables - 1 &&
          config.domePortMappings[1][0] == 1,
        "configuration did not publish immutable mapping values");

      using var stream = new MemoryStream();
      var serializer =
        new XSerializer.XmlSerializer<global::Spectrum.SpectrumConfigurationDocument>();
      serializer.Serialize(
        stream,
        global::Spectrum.SpectrumConfigurationDocument.FromConfiguration(
          config));
      stream.Position = 0;
      global::Spectrum.SpectrumConfiguration restored =
        serializer.Deserialize(stream).ToConfiguration();
      Assert(restored.domePortMappings.Length ==
          LEDDomeOutput.NumDomeBoxes &&
          restored.domePortMappings[4].SequenceEqual(
            Enumerable.Range(0, PortsPerController).Select(
              port => (port + 4) % PortsPerController)),
        "per-box port mappings did not survive serialization");

    }

    private static DomePortMapping[] PortMappingDtos(
      IReadOnlyList<int[]> mappings
    ) => mappings.Select(mapping => new DomePortMapping(mapping)).ToArray();

    private static void AssertBoxPortMapping(
      int[] actual, int[] identity, int box, int[] mapping, string context
    ) {
      for (int physicalPort = 0;
          physicalPort < PortsPerController; physicalPort++) {
        int legacyPath = mapping[physicalPort];
        int expectedStart =
          (box * PortsPerController + legacyPath) * PortPixels;
        int actualStart =
          (box * PortsPerController + physicalPort) * PortPixels;
        for (int offset = 0; offset < PortPixels; offset++) {
          Assert(actual[actualStart + offset] == identity[expectedStart + offset],
            context + " mismatch at box " + box + ", port " +
            physicalPort + ", pixel " + offset);
        }
      }
    }

    private static void TwoStageCalibrationContract() {
      int[] identityPorts = Enumerable.Range(
        0, PortsPerController).ToArray();
      var missingPortConfig = new global::Spectrum.SpectrumConfiguration();
      missingPortConfig.ReplaceDomeCableMapping(Enumerable.Range(
        0, LEDDomeOutput.NumCables).ToArray());
      var missingPortController =
        new global::Spectrum.Web.DomeCalibrationController(
          new ImmediateGateway(), missingPortConfig,
          new global::Spectrum.DomeCalibrationState(),
          LEDDomeOutput.NumCables);
      var missingPortState =
        missingPortController.StartAsync().GetAwaiter().GetResult();
      Assert(!missingPortState.hasSavedMapping &&
          missingPortState.savedPortMappings.All(mapping =>
            mapping.SequenceEqual(identityPorts)) &&
          missingPortState.stripMappings.All(mapping =>
            mapping.SequenceEqual(identityPorts)),
        "missing five-box mappings did not start from identity guesses");
      missingPortController.CancelAsync().GetAwaiter().GetResult();

      int[] savedCables = { 2, 0, 1, 3, 4, 5, 6, 7, 8, 9 };
      int[][] savedPorts = Enumerable.Range(
        0, LEDDomeOutput.NumDomeBoxes).Select(box =>
          Enumerable.Range(0, PortsPerController).Select(
            port => (port + box + 3) % PortsPerController).ToArray())
        .ToArray();
      var config = new global::Spectrum.SpectrumConfiguration();
      config.ReplaceDomeCableMapping(savedCables);
      config.ReplaceDomePortMappings(PortMappingDtos(savedPorts));
      var renderState = new global::Spectrum.DomeCalibrationState();
      var controller = new global::Spectrum.Web.DomeCalibrationController(
        new ImmediateGateway(), config, renderState,
        LEDDomeOutput.NumCables);

      var state = controller.StartAsync().GetAwaiter().GetResult();
      Assert(state.stage == "cables" && state.currentStep == 0 &&
          state.currentCandidate == savedCables[0] &&
          state.rawControllerCable == 0 &&
          state.simulatorEndpoint == savedCables[0] &&
          state.cableReadout.Contains("[") &&
          state.cableReadout.Contains("~"),
        "Stage 1 did not start from the saved guess on raw cable 1A");

      var rejectedSave = controller.SaveAsync().GetAwaiter().GetResult();
      Assert(!rejectedSave.ok &&
          config.domeCableMapping.SequenceEqual(savedCables),
        "a partial calibration was persisted");

      state = controller.NavigateAsync(1).GetAwaiter().GetResult();
      Assert(state.rawControllerCable == 0 &&
          state.currentCandidate != savedCables[0],
        "Stage 1 candidate navigation changed the raw cable");
      state = controller.NavigateAsync(-1).GetAwaiter().GetResult();
      Assert(state.currentCandidate == savedCables[0],
        "Stage 1 Previous did not return to the saved candidate");
      state = controller.ConfirmAsync().GetAwaiter().GetResult();
      state = controller.CancelAsync().GetAwaiter().GetResult();
      Assert(state.stage == "idle" && !renderState.Active &&
          state.picks.SequenceEqual(savedCables) &&
          state.cableConfirmed.All(value => !value) &&
          state.stripConfirmed.SelectMany(row => row).All(value => !value) &&
          state.cableReadout.Contains("~") &&
          config.domeCableMapping.SequenceEqual(savedCables),
        "Cancel did not discard the draft and restore saved guesses");
      state = controller.StartAsync().GetAwaiter().GetResult();
      Assert(state.currentCandidate == savedCables[0],
        "a new run after Cancel did not restart from the saved guess");
      state = controller.ConfirmAsync().GetAwaiter().GetResult();
      state = controller.BackAsync().GetAwaiter().GetResult();
      Assert(state.currentStep == 0 &&
          state.currentCandidate == savedCables[0],
        "Back did not release and restore the preceding endpoint guess");

      for (int cable = 0; cable < LEDDomeOutput.NumCables; cable++) {
        state = controller.ConfirmAsync().GetAwaiter().GetResult();
      }
      Assert(state.cablesComplete && state.stage == "cables" &&
          state.currentStep == LEDDomeOutput.NumCables &&
          state.rawControllerCable == -1,
        "Stage 1 did not end in a blank review state");

      state = controller.ConfirmAsync().GetAwaiter().GetResult();
      int expectedControllerCable = Array.IndexOf(savedCables, 0);
      int expectedRawPort = (expectedControllerCable % 2) * 4;
      Assert(state.stage == "strips" && state.selectedBox == 0 &&
          state.rawControllerCable == expectedControllerCable &&
          state.rawControllerBox == expectedControllerCable / 2 &&
          state.rawControllerPort == expectedRawPort &&
          state.currentCandidate == savedPorts[0][0],
        "Stage 2 did not derive its raw port from the Stage 1 draft");

      int fixedRawCable = state.rawControllerCable;
      int fixedRawPort = state.rawControllerPort;
      state = controller.NavigateAsync(1).GetAwaiter().GetResult();
      Assert(state.rawControllerCable == fixedRawCable &&
          state.rawControllerPort == fixedRawPort &&
          state.currentCandidate != savedPorts[0][0],
        "Stage 2 candidate navigation changed the raw controller port");
      state = controller.NavigateAsync(-1).GetAwaiter().GetResult();
      Assert(state.currentCandidate == savedPorts[0][0],
        "Stage 2 Previous did not return to its per-box saved guess");

      for (int port = 0; port < PortsPerController; port++) {
        state = controller.ConfirmAsync().GetAwaiter().GetResult();
      }
      Assert(state.stripSteps[0] == PortsPerController &&
          state.canApplyBoxOne,
        "Box 1 completion did not offer the copy action");
      state = controller.ApplyBoxOneAsync().GetAwaiter().GetResult();
      Assert(state.stage == "review" && state.saveable &&
          state.copiedFromBoxOne.Skip(1).All(value => value) &&
          state.stripMappings.Skip(1).All(mapping =>
            mapping.SequenceEqual(state.stripMappings[0])) &&
          state.stripReadout.Contains("copied from Box 1"),
        "Apply Box 1 did not create five complete independent drafts");

      int[] untouchedBoxFour =
        (int[])state.stripMappings[3].Clone();
      state = controller.RecalibrateBoxAsync(2).GetAwaiter().GetResult();
      state = controller.NavigateAsync(1).GetAwaiter().GetResult();
      state = controller.ConfirmAsync().GetAwaiter().GetResult();
      Assert(!state.stripMappings[2].SequenceEqual(state.stripMappings[3]) &&
          state.stripMappings[3].SequenceEqual(untouchedBoxFour) &&
          !state.copiedFromBoxOne[2] && state.copiedFromBoxOne[3],
        "recalibrating Box 3 mutated another copied box");
      for (int port = 1; port < PortsPerController; port++) {
        state = controller.ConfirmAsync().GetAwaiter().GetResult();
      }
      Assert(state.stage == "review" && state.saveable,
        "recalibrated Box 3 did not return to a saveable permutation");

      var result = controller.SaveAsync().GetAwaiter().GetResult();
      Assert(result.ok && result.state.stage == "idle" &&
          !renderState.Active &&
          result.state.cableConfirmed.All(value => !value) &&
          result.state.cableReadout.Contains("~") &&
          config.domeCableMapping.SequenceEqual(state.picks) &&
          config.domePortMappings.Length == LEDDomeOutput.NumDomeBoxes &&
          config.domePortMappings[2].SequenceEqual(
            state.stripMappings[2]),
        "final Save did not atomically commit and release the calibration");

      result.state.stripMappings[2][0] = -1;
      Assert(config.domePortMappings[2][0] >= 0 &&
          controller.State().stripMappings[2][0] >= 0,
        "a returned calibration snapshot aliases the draft or configuration");
    }

    private static void CalibrationDiagnosticFrames() {
      using var sink = new LoopbackSink();
      global::Spectrum.SpectrumConfiguration config;
      LEDDomeOutput output = ConnectDome(sink, null, out config);
      config.domeSimulationEnabled = true;
      output.SimulatorHasConsumer = true;
      config.domeMaxBrightness = 1;
      config.ReplaceDomePortMappings(PortMappingDtos(Enumerable.Range(
        0, LEDDomeOutput.NumDomeBoxes).Select(_ =>
          Enumerable.Range(0, PortsPerController).ToArray()).ToArray()));
      var state = new global::Spectrum.DomeCalibrationState();
      var visualizer =
        new global::Spectrum.LEDDomeMappingCalibrationVisualizer(
          config, state, output);
      try {
        state.ShowCable(0, 4);
        visualizer.Visualize();
        Send(output.OperatorUpdate);
        byte[] firstHardware = sink.ReceiveMessage();
        HashSet<int> firstSimulator = DrainLitSimulatorStruts(output);
        var expectedFirstSimulator = new HashSet<int>(
          LEDDomeOutput.GetPhysicalCableStruts(
            2, 0, Enumerable.Range(0, PortsPerController).ToArray()));
        Assert(firstSimulator.SetEquals(expectedFirstSimulator),
          "the simulator did not show the logical Stage 1 candidate " +
          "(expected " + expectedFirstSimulator.Count + " struts, got " +
          firstSimulator.Count + "; missing " +
          string.Join(",", expectedFirstSimulator.Except(firstSimulator)) +
          "; extra " +
          string.Join(",", firstSimulator.Except(expectedFirstSimulator)) + ")");

        state.ShowCable(0, 5);
        visualizer.Visualize();
        output.FlushHardware();
        Send(output.OperatorUpdate);
        byte[] secondHardware = sink.ReceiveMessage();
        HashSet<int> secondSimulator = DrainLitSimulatorStruts(output);
        Assert(firstHardware.SequenceEqual(secondHardware),
          "candidate navigation changed the raw OPC frame");
        Assert(secondSimulator.SetEquals(
            LEDDomeOutput.GetPhysicalCableStruts(
              2, 1, Enumerable.Range(0, PortsPerController).ToArray())),
          "candidate navigation did not republish the logical simulator frame");

        // Stage 2 must likewise keep the derived raw controller port fixed
        // while candidate navigation republishes a different logical strip in
        // the selected dome-side box.
        const int rawControllerBox = 3;
        const int rawControllerPort = 6;
        const int simulatorBox = 1;
        state.ShowPort(
          rawControllerBox * 2 + rawControllerPort / StrandsPerCable,
          rawControllerBox,
          rawControllerPort,
          simulatorBox,
          2);
        visualizer.Visualize();
        Send(output.OperatorUpdate);
        byte[] firstPortHardware = sink.ReceiveMessage();
        int[] firstPortPixels = PayloadPixels(firstPortHardware);
        int firstPortPixel =
          (rawControllerBox * PortsPerController + rawControllerPort) *
          PortPixels;
        int expectedLitPortPixels = LEDDomeOutput.GetStripPathStruts(
          rawControllerBox, rawControllerPort).Sum(LEDDomeOutput.GetNumLEDs);
        int[] actualLitPortPixels = Enumerable.Range(
          0, firstPortPixels.Length).Where(
            pixel => firstPortPixels[pixel] != 0).ToArray();
        Assert(actualLitPortPixels.Length == expectedLitPortPixels &&
            actualLitPortPixels.All(pixel =>
              pixel >= firstPortPixel &&
              pixel < firstPortPixel + PortPixels),
          "Stage 2 did not illuminate only its derived raw controller port");
        HashSet<int> firstPortSimulator = DrainLitSimulatorStruts(output);
        Assert(firstPortSimulator.SetEquals(
            LEDDomeOutput.GetStripPathStruts(simulatorBox, 2)),
          "Stage 2 simulator did not show its logical strip candidate");

        state.ShowPort(
          rawControllerBox * 2 + rawControllerPort / StrandsPerCable,
          rawControllerBox,
          rawControllerPort,
          simulatorBox,
          5);
        visualizer.Visualize();
        output.FlushHardware();
        Send(output.OperatorUpdate);
        byte[] secondPortHardware = sink.ReceiveMessage();
        HashSet<int> secondPortSimulator = DrainLitSimulatorStruts(output);
        Assert(firstPortHardware.SequenceEqual(secondPortHardware),
          "Stage 2 candidate navigation changed the raw OPC port frame");
        Assert(secondPortSimulator.SetEquals(
            LEDDomeOutput.GetStripPathStruts(simulatorBox, 5)),
          "Stage 2 candidate navigation did not republish the logical strip");

        state.Deactivate();
        Assert(state.ShouldOverride,
          "deactivation did not retain one final blanking tick");
        visualizer.Visualize();
        Send(output.OperatorUpdate);
        int[] blanked = PayloadPixels(sink.ReceiveMessage());
        Assert(blanked.All(color => color == 0) && !state.ShouldOverride,
          "deactivation did not flush a black raw frame before releasing");
      } finally {
        output.SimulatorHasConsumer = false;
        state.Deactivate();
        sink.CloseConnection();
        output.Active = false;
      }
    }

    private static HashSet<int> DrainLitSimulatorStruts(
      LEDDomeOutput output
    ) {
      var lit = new HashSet<int>();
      while (output.SimulatorCommandQueue.TryDequeue(out DomeLEDCommand command)) {
        if (command.isFlush) {
          continue;
        }
        if (command.color == 0) {
          lit.Remove(command.strutIndex);
        } else {
          lit.Add(command.strutIndex);
        }
      }
      return lit;
    }

    private sealed class ImmediateGateway : ApplicationStateDispatcher {
      public bool CheckAccess() => true;
      public void Post(Action mutation) => mutation();
      public Task InvokeAsync(Action mutation) {
        mutation();
        return Task.CompletedTask;
      }
      public Task<T> InvokeAsync<T>(Func<T> read) =>
        Task.FromResult(read());
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
      using FileStream? capture = OpenCapture(
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
      var opc = new OPCAPI(
        address, separateThread, _ => { }, TimeSpan.Zero);
      opc.Active = true;
      WaitHandle? pendingConnect = separateThread
        ? null
        : opc.PendingConnectWaitHandle;
      sink.Accept();
      if (!separateThread) {
        Assert(pendingConnect != null &&
            pendingConnect.WaitOne(TimeSpan.FromSeconds(2)),
          "loopback OPC connection did not complete");
      }
      return opc;
    }

    private static LEDDomeOutput ConnectDome(
      LoopbackSink sink, int[]? cableMapping,
      out global::Spectrum.SpectrumConfiguration config
    ) {
      config = new global::Spectrum.SpectrumConfiguration {
        domeEnabled = true,
        domeOutputInSeparateThread = false,
        domeBeagleboneOPCAddress = "127.0.0.1:" + sink.Port,
      };
      config.ReplaceDomeCableMapping(cableMapping);
      var output = new LEDDomeOutput(
        config, new RuntimeTelemetry(), new BeatBroadcaster(config),
        TimeSpan.Zero);
      output.Active = true;
      WaitHandle? pendingConnect = output.PendingOpcConnectWaitHandle;
      sink.Accept();
      Assert(pendingConnect != null &&
          pendingConnect.WaitOne(TimeSpan.FromSeconds(2)),
        "loopback dome OPC connection did not complete");
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
      using FileStream? capture = OpenCapture(name);
      capture?.Write(message, 0, message.Length);
    }

    private static FileStream? OpenCapture(string name) {
      string? directory = Environment.GetEnvironmentVariable(
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

    private static void Assert(
      [DoesNotReturnIf(false)] bool condition, string? message
    ) {
      if (!condition) {
        throw new InvalidOperationException(message);
      }
    }

    private sealed class LoopbackSink : IDisposable {
      private readonly TcpListener listener;
      private readonly Task<Socket> accepting;
      private Socket? socket;

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
        Socket socket = this.socket ?? throw new InvalidOperationException(
          "loopback OPC client was not accepted");
        var bytes = new byte[count];
        int received = 0;
        while (received < count) {
          int read = socket.Receive(
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

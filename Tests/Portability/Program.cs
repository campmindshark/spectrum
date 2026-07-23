using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Testing.Platform.Builder;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Platform.Linux;
using Spectrum.Visualizers;
using Spectrum.Web;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.Portability.Tests {

  [TestClass]
  public sealed class PortabilityTests {
    private static readonly IReadOnlyDictionary<string, Action> TestCases =
      BuildTestCases();

    private static async Task<int> Main(string[] args) {
      if (args.Length == 1 && args[0] == "--fake-pcm-tracker") {
        return RunFakePcmTracker();
      }

      ITestApplicationBuilder builder =
        await TestApplication.CreateBuilderAsync(args);
      SelfRegisteredExtensions.AddSelfRegisteredExtensions(builder, args);
      using ITestApplication application = await builder.BuildAsync();
      return await application.RunAsync();
    }

    public static IEnumerable<object[]> DiscoverTestCases() =>
      TestCases.Keys.OrderBy(name => name)
        .Select(name => new object[] { name });

    [TestMethod]
    [DoNotParallelize]
    [DynamicData(nameof(DiscoverTestCases))]
    public void Run(string name) {
      TestCases[name]();
    }

    private static IReadOnlyDictionary<string, Action> BuildTestCases() {
      var tests = new Dictionary<string, Action>();
      foreach (MethodInfo method in typeof(PortabilityTests).GetMethods(
          BindingFlags.NonPublic | BindingFlags.Static)) {
        if (method.ReturnType == typeof(void) &&
            method.GetParameters().Length == 0) {
          tests.Add(method.Name, () => Invoke(method));
        }
      }
      return tests;
    }

    private static void Invoke(MethodInfo method) {
      try {
        method.Invoke(null, null);
      } catch (TargetInvocationException error)
          when (error.InnerException != null) {
        ExceptionDispatchInfo.Capture(error.InnerException).Throw();
      }
    }

    private static void DedicatedThreadOwnership() {
      int callerThread = Environment.CurrentManagedThreadId;
      using var dispatcher =
        new DedicatedThreadApplicationStateDispatcher();
      int ownerThread = dispatcher.InvokeAsync(() => {
        Assert(dispatcher.CheckAccess(),
          "CheckAccess was false on the state-owner thread");
        dispatcher.InvokeAsync(() => {
          Assert(dispatcher.CheckAccess(),
            "a reentrant invocation left the state-owner thread");
        }).GetAwaiter().GetResult();
        return Environment.CurrentManagedThreadId;
      }).GetAwaiter().GetResult();

      Assert(ownerThread != callerThread,
        "application state remained on the calling thread");
      Assert(!dispatcher.CheckAccess(),
        "CheckAccess was true away from the state-owner thread");
    }

    private static void DedicatedThreadSerialization() {
      using var dispatcher =
        new DedicatedThreadApplicationStateDispatcher();
      const int producerCount = 8;
      const int commandsPerProducer = 100;
      int value = 0;
      var producers = new Task[producerCount];
      for (int producer = 0; producer < producers.Length; producer++) {
        producers[producer] = Task.Run(() => {
          for (int command = 0; command < commandsPerProducer; command++) {
            dispatcher.Post(() => value++);
          }
        });
      }
      Task.WhenAll(producers).GetAwaiter().GetResult();

      int captured = dispatcher.InvokeAsync(() => value)
        .GetAwaiter().GetResult();
      Assert(captured == producerCount * commandsPerProducer,
        "the serialized state lost a concurrent command");
    }

    private static void DedicatedThreadFailureContainment() {
      var reported = new TaskCompletionSource<Exception>(
        TaskCreationOptions.RunContinuationsAsynchronously);
      using var dispatcher = new DedicatedThreadApplicationStateDispatcher(
        reportUnhandledException: error => reported.TrySetResult(error));
      dispatcher.Post(() => throw new InvalidOperationException("posted"));
      Exception postError = reported.Task.WaitAsync(TimeSpan.FromSeconds(2))
        .GetAwaiter().GetResult();
      Assert(postError is InvalidOperationException &&
          postError.Message == "posted",
        "a failed Post command was not reported");

      Task failedInvocation = dispatcher.InvokeAsync(
        () => throw new InvalidOperationException("invoked"));
      try {
        failedInvocation.GetAwaiter().GetResult();
        throw new InvalidOperationException(
          "a failed InvokeAsync command completed successfully");
      } catch (InvalidOperationException error) {
        Assert(error.Message == "invoked",
          "InvokeAsync returned the wrong failure");
      }

      int result = dispatcher.InvokeAsync(() => 42).GetAwaiter().GetResult();
      Assert(result == 42,
        "a failed command terminated the state-owner thread");
    }

    private static void DedicatedThreadShutdown() {
      var dispatcher = new DedicatedThreadApplicationStateDispatcher();
      int value = 0;
      for (int i = 0; i < 50; i++) {
        dispatcher.Post(() => value++);
      }
      dispatcher.Dispose();
      Assert(value == 50,
        "shutdown discarded an accepted state command");

      try {
        dispatcher.Post(() => { });
        throw new InvalidOperationException(
          "the dispatcher accepted a command after shutdown");
      } catch (ObjectDisposedException) {
      }
    }

    private static void ConfigurationStoreRecovery() {
      string directory = Path.Combine(
        Path.GetTempPath(), "spectrum-portability-" + Guid.NewGuid());
      Directory.CreateDirectory(directory);
      try {
        string primary = Path.Combine(directory, "config.txt");
        string backup = Path.Combine(directory, "config.old.txt");
        string defaults = Path.Combine(directory, "config.default.txt");
        File.WriteAllText(defaults, "packaged");
        var store = new ConfigurationFileStore<string>(
          primary, backup, defaults,
          (stream, value) => {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            stream.Write(bytes);
          },
          stream => {
            using var reader = new StreamReader(
              stream, Encoding.UTF8, true, 1024, leaveOpen: true);
            string value = reader.ReadToEnd();
            if (value.StartsWith("!invalid!")) {
              throw new InvalidDataException("invalid test configuration");
            }
            return value;
          });

        ConfigurationLoadResult<string> packaged =
          store.Load(() => "empty");
        Assert(packaged.Value == "packaged" &&
            packaged.SourcePath == defaults,
          "the packaged default was not selected");

        store.Save("first");
        store.Save("second");
        ConfigurationLoadResult<string> current =
          store.Load(() => "empty");
        Assert(current.Value == "second" && current.SourcePath == primary,
          "the primary configuration did not contain the latest save");
        Assert(File.ReadAllText(backup) == "first",
          "atomic replacement did not preserve the prior configuration");
        Assert(!File.Exists(store.TemporaryPath),
          "the save left its temporary file behind");

        File.WriteAllText(primary, "!invalid!primary");
        ConfigurationLoadResult<string> recovered =
          store.Load(() => "empty");
        Assert(recovered.Value == "first" &&
            recovered.SourcePath == backup,
          "an invalid primary configuration did not recover from backup");
        Assert(recovered.Failures.Count == 1 &&
            recovered.Failures[0].Path == primary,
          "the failed primary load was not reported");
      } finally {
        Directory.Delete(directory, recursive: true);
      }
    }

    private static void MadmomRuntimeDiscovery() {
      string directory = Path.Combine(
        Path.GetTempPath(), "spectrum-madmom-" + Guid.NewGuid());
      string nested = Path.Combine(directory, "publish", "nested");
      Directory.CreateDirectory(nested);
      try {
        string windowsEnvironment = Path.Combine(
          directory, "Madmom", ".build-env");
        string windowsScripts = Path.Combine(
          windowsEnvironment, "Scripts");
        Directory.CreateDirectory(windowsScripts);
        string windowsPython = Path.Combine(
          windowsScripts, "python.exe");
        string windowsTracker = Path.Combine(
          windowsScripts, "DBNBeatTracker");
        File.WriteAllText(windowsPython, "");
        File.WriteAllText(windowsTracker, "");

        MadmomRuntimePaths? windows = MadmomRuntimeLocator.Find(
          nested, useWindowsLayout: true);
        Assert(windows != null &&
            windows.PythonPath == windowsPython &&
            windows.TrackerPath == windowsTracker,
          "the Windows virtual-environment layout was not found");

        string packagedWindowsEnvironment = Path.Combine(
          directory, "Madmom", "runtime");
        string packagedWindowsScripts = Path.Combine(
          packagedWindowsEnvironment, "Scripts");
        Directory.CreateDirectory(packagedWindowsScripts);
        string packagedWindowsPython = Path.Combine(
          packagedWindowsEnvironment, "python.exe");
        string packagedWindowsTracker = Path.Combine(
          packagedWindowsScripts, "DBNBeatTracker");
        File.WriteAllText(packagedWindowsPython, "");
        File.WriteAllText(packagedWindowsTracker, "");

        windows = MadmomRuntimeLocator.Find(
          nested, useWindowsLayout: true);
        Assert(windows != null &&
            windows.PythonPath == packagedWindowsPython &&
            windows.TrackerPath == packagedWindowsTracker,
          "the packaged Windows layout was not preferred");

        string unixEnvironment = Path.Combine(
          directory, "Madmom", "runtime");
        string unixBin = Path.Combine(unixEnvironment, "bin");
        Directory.CreateDirectory(unixBin);
        string unixPython = Path.Combine(unixBin, "python");
        string unixTracker = Path.Combine(unixBin, "DBNBeatTracker");
        File.WriteAllText(unixPython, "");
        File.WriteAllText(unixTracker, "");

        MadmomRuntimePaths? unix = MadmomRuntimeLocator.Find(
          nested, useWindowsLayout: false);
        Assert(unix != null &&
            unix.PythonPath == unixPython &&
            unix.TrackerPath == unixTracker,
          "the packaged Unix runtime layout was not found");

        MadmomRuntimePaths? missing = MadmomRuntimeLocator.Find(
          Path.Combine(
            Path.GetTempPath(),
            "spectrum-madmom-missing-" + Guid.NewGuid()),
          useWindowsLayout: false);
        Assert(missing == null,
          "runtime discovery escaped its ancestor search roots");
      } finally {
        Directory.Delete(directory, recursive: true);
      }
    }

    private static void PortableCoreLayerConfiguration() {
      var config = new global::Spectrum.SpectrumConfiguration();
      config.ReplaceDomeLayerStack(new[] {
        new DomeLayerSettings {
          InstanceId = "portable-radial",
          VisualizerKey = "radial",
          RendererParams = new Dictionary<string, double> {
            ["size"] = 0.25,
          },
        },
      });

      LayerStackSnapshot snapshot =
        ((ILayerStackSnapshotSource)config).DomeLayerStackSnapshot;
      Assert(snapshot.Layers.Length == 1 &&
          snapshot.Layers[0].Id.Value == "portable-radial",
        "the portable configuration did not publish its layer snapshot");
      LayerDefinition? definition =
        global::Spectrum.BuiltInDomeLayerCatalog.Metadata.Get("radial");
      Assert(definition != null,
        "the portable built-in catalog omitted the radial definition");
      Assert(definition.CompileOptions(
            snapshot.Layers[0].RendererParameters)
          is RadialLayerOptions options && options.Size == 0.25,
        "the portable built-in catalog did not compile typed layer options");
    }

    private static void LinuxAlsaAudioInput() {
      var config = new global::Spectrum.SpectrumConfiguration {
        audioDeviceID = "hw:test,0",
      };
      var api = new FakeAlsaApi();
      var tracker = new FakePcmBeatTracker();
      using var input = new AlsaAudioLevelInput(
        config, api, tracker, TimeSpan.FromMilliseconds(5));

      IReadOnlyList<AudioCaptureDevice> devices =
        input.GetAvailableDevices();
      Assert(devices.Count == 1 &&
          devices[0].Id == "hw:test,0" &&
          devices[0].Name == "Test capture",
        "the ALSA device identity was not exposed unchanged");

      input.Active = true;
      Assert(api.FrameProcessed.Wait(TimeSpan.FromSeconds(2)) &&
          input.Volume > 0.99f,
        "signed 16-bit PCM did not publish a normalized peak level");
      Assert(api.OpenCount > 0 && input.LastError == null,
        "the ALSA worker did not open the configured device cleanly");
      input.Active = false;
      Assert(input.Volume == 0,
        "stopping ALSA capture left a stale peak level");

      api.FailOpen = true;
      var failingTracker = new FakePcmBeatTracker();
      using var failing = new AlsaAudioLevelInput(
        config, api, failingTracker, TimeSpan.FromMilliseconds(5));
      failing.Active = true;
      Assert(api.RepeatedOpenFailure.Wait(TimeSpan.FromSeconds(2)) &&
          failing.LastError?.Contains("test device unavailable") == true,
        "an ALSA open failure was not reported");
      failing.GetAvailableDevices();
      Assert(failing.LastError?.Contains("test device unavailable") == true,
        "device polling erased the active ALSA capture failure");
      failing.Active = false;
    }

    private static void LinuxMadmomPcmInput() {
      var config = new global::Spectrum.SpectrumConfiguration {
        audioDeviceID = "hw:test,0",
        beatInput = 1,
      };
      var api = new FakeAlsaApi();
      var tracker = new FakePcmBeatTracker {
        LastErrorValue = "test tracker unavailable",
      };
      using var input = new AlsaAudioLevelInput(
        config, api, tracker, TimeSpan.FromMilliseconds(5));
      input.Active = true;
      Assert(tracker.Written.Wait(TimeSpan.FromSeconds(2)) &&
          tracker.WriteCount > 0,
        "the selected Madmom source did not receive ALSA PCM");
      Assert(tracker.Enabled && tracker.LastChannels == 2 &&
          tracker.LastSampleCount > 0,
        "the ALSA worker published the wrong PCM shape to Madmom");
      Assert(input.LastError == "test tracker unavailable",
        "the audio health surface hid the Madmom process error");

      tracker.Disabled.Reset();
      config.beatInput = 0;
      Assert(tracker.Disabled.Wait(TimeSpan.FromSeconds(2)) &&
          !tracker.Enabled,
        "changing tempo source did not stop the Madmom process");
      Assert(input.LastError == null,
        "a disabled Madmom source left a stale audio health error");
      input.Active = false;

      short[] stereo = {
        short.MaxValue, -short.MaxValue,
        short.MinValue, short.MinValue,
      };
      byte[]? encoded = null;
      int byteCount = MadmomPcmBeatTracker.EncodeMonoPcm(
        stereo, stereo.Length, channels: 2, ref encoded);
      Assert(byteCount == 4 && encoded != null &&
          encoded[0] == 0 && encoded[1] == 0 &&
          encoded[2] == 0 && encoded[3] == 0x80,
        "stereo ALSA PCM was not downmixed to signed-16-bit little endian");

      Assert(MadmomPcmBeatTracker.TryParseBeat(
          "BEAT:12.345", out long milliseconds) &&
          milliseconds == 12345 &&
          !MadmomPcmBeatTracker.TryParseBeat("BEAT:not-a-number", out _),
        "Madmom stdout beat parsing accepted an invalid event");
      var runtime = new MadmomRuntimePaths(
        "/runtime/bin/python", "/runtime/bin/DBNBeatTracker");
      System.Diagnostics.ProcessStartInfo start =
        MadmomPcmBeatTracker.CreateStartInfo(runtime);
      Assert(start.RedirectStandardInput &&
          start.RedirectStandardOutput &&
          start.RedirectStandardError &&
          start.ArgumentList.Count == 3 &&
          start.ArgumentList[0] == runtime.TrackerPath &&
          start.ArgumentList[1] == "--pcm_stdin" &&
          start.ArgumentList[2] == "online",
        "the Linux Madmom child was not configured for owned PCM stdin");
    }

    private static void LinuxMadmomProcessLifecycle() {
      var config = new global::Spectrum.SpectrumConfiguration {
        audioDeviceID = "hw:test,0",
        beatInput = 1,
      };
      var beat = new BeatBroadcaster(config);
      using var tracker = new MadmomPcmBeatTracker(
        beat,
        CreateFakePcmTrackerStartInfo,
        TimeSpan.FromSeconds(10));
      var api = new FakeAlsaApi();
      using var input = new AlsaAudioLevelInput(
        config, api, tracker, TimeSpan.FromMilliseconds(5));
      using var beatReceived = new ManualResetEventSlim();
      using var trackerFailed = new ManualResetEventSlim();
      beat.PropertyChanged += (_, change) => {
        if (change.PropertyName == "BPMString" &&
            beat.MeasureLength == 500) {
          beatReceived.Set();
        }
      };
      tracker.StatusChanged += () => {
        if (!string.IsNullOrWhiteSpace(tracker.LastError)) {
          trackerFailed.Set();
        }
      };

      input.Active = true;
      Assert(beatReceived.Wait(TimeSpan.FromSeconds(5)) &&
          beat.MeasureLength == 500,
        "Madmom child stdout did not publish its two 120 BPM beat events");
      Assert(trackerFailed.Wait(TimeSpan.FromSeconds(5)) &&
          !string.IsNullOrWhiteSpace(input.LastError),
        "an unexpected Madmom child exit was not reported");
      string? lastError = input.LastError;
      Assert(lastError != null &&
          lastError.Contains("Madmom", StringComparison.Ordinal),
        "the Madmom child failure was reported as the wrong health error: " +
        lastError);
      input.Active = false;
    }

    private static System.Diagnostics.ProcessStartInfo
      CreateFakePcmTrackerStartInfo() {
      string executable = Environment.ProcessPath ??
        throw new InvalidOperationException(
          "The portability test process path is unavailable.");
      var start = new System.Diagnostics.ProcessStartInfo {
        FileName = executable,
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
      };
      if (string.Equals(
          Path.GetFileNameWithoutExtension(executable),
          "dotnet",
          StringComparison.OrdinalIgnoreCase)) {
        start.ArgumentList.Add(
          typeof(PortabilityTests).Assembly.Location);
      }
      start.ArgumentList.Add("--fake-pcm-tracker");
      return start;
    }

    private static int RunFakePcmTracker() {
      var buffer = new byte[4];
      int received = 0;
      Stream input = Console.OpenStandardInput();
      while (received < buffer.Length) {
        int count = input.Read(buffer, received, buffer.Length - received);
        if (count == 0) {
          return 3;
        }
        received += count;
      }
      Console.WriteLine("BEAT:0.500");
      Console.WriteLine("BEAT:1.000");
      Console.Out.Flush();
      return 0;
    }

    private static void PortableProDjLinkInput() {
      using var blocker = new UdpClient();
      blocker.ExclusiveAddressUse = true;
      blocker.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
      IPEndPoint localEndpoint = blocker.Client.LocalEndPoint as IPEndPoint ??
        throw new InvalidOperationException(
          "the UDP blocker did not receive a local endpoint");
      int port = localEndpoint.Port;

      var config = new global::Spectrum.SpectrumConfiguration {
        beatInput = 2,
      };
      var beat = new BeatBroadcaster(config);
      using var input = new global::Spectrum.ProDjLinkInput(
        config,
        beat,
        connectNetwork: true,
        port,
        TimeSpan.FromMilliseconds(5));
      using var failureReported = new ManualResetEventSlim();
      using var listening = new ManualResetEventSlim();
      using var beatReceived = new ManualResetEventSlim();
      input.StatusChanged += () => {
        if (input.LastError != null) {
          failureReported.Set();
        }
        if (input.Listening) {
          listening.Set();
        }
      };
      beat.PropertyChanged += (_, change) => {
        if (change.PropertyName == "BPMString") {
          beatReceived.Set();
        }
      };
      Assert(input.Enabled,
        "the Pro DJ Link input did not follow the selected tempo source");
      input.Active = true;
      Assert(failureReported.Wait(TimeSpan.FromSeconds(2)),
        "a busy Pro DJ Link port did not report a contained bind error");
      blocker.Dispose();
      Assert(listening.Wait(TimeSpan.FromSeconds(2)),
        "the portable Pro DJ Link listener did not recover after the port " +
        "became available");

      byte[] packet = new byte[0x5d];
      byte[] magic = {
        0x51, 0x73, 0x70, 0x74, 0x31,
        0x57, 0x6d, 0x4a, 0x4f, 0x4c,
      };
      Array.Copy(magic, packet, magic.Length);
      packet[0x0a] = 0x28;
      packet[0x21] = 1;
      packet[0x55] = 0x10; // pitch 0x100000 == 1.0x
      packet[0x5a] = 0x2e; // 12000 == 120.00 BPM
      packet[0x5b] = 0xe0;
      packet[0x5c] = 3;

      using var sender = new UdpClient();
      var endpoint = new IPEndPoint(IPAddress.Loopback, port);
      sender.Send(packet, packet.Length, endpoint);
      Assert(beatReceived.Wait(TimeSpan.FromSeconds(2)) &&
          beat.MeasureLength == 500 &&
          beat.LatestBeatWithinBar == 3,
        "the portable UDP input did not publish the 120 BPM beat packet");
      Assert(input.LastError == null,
        "the Pro DJ Link listener reported an unexpected error");

      input.Active = false;
      Assert(!input.Listening,
        "stopping Pro DJ Link left its UDP socket published");
      config.beatInput = 0;
      Assert(!input.Enabled,
        "the Pro DJ Link input stayed enabled for another tempo source");
    }

    private static void PortableRuntimeAssemblyBoundary() {
      var assembly = typeof(global::Spectrum.Operator).Assembly;
      var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "Audio",
        "MIDI",
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase",
      };
      foreach (var reference in assembly.GetReferencedAssemblies()) {
        Assert(reference.Name == null || !forbidden.Contains(reference.Name),
          "portable runtime referenced " + reference.Name);
      }

      using var dispatcher =
        new DedicatedThreadApplicationStateDispatcher();
      var runtime = new global::Spectrum.Operator(
        new global::Spectrum.SpectrumConfiguration(),
        dispatcher,
        new DisabledSpectrumInputFactory());
      Assert(runtime.AudioInput.Volume == 0 &&
          runtime.MidiInput.MidiLog != null,
        "the portable disabled inputs were not composed");
    }

    private static void PortableEarthTextureDecoder() {
      using Stream? texture = typeof(LEDDomeEarthVisualizer).Assembly
        .GetManifestResourceStream(
          LEDDomeEarthVisualizer.TextureResourceName);
      Assert(texture != null, "the Earth texture was not embedded in core");
      global::Spectrum.PortablePngImage image =
        global::Spectrum.PortablePngImage.Load(texture);
      Assert(image.Width == 1774 && image.Height == 887,
        "the Earth texture dimensions changed during portable decoding");
      Assert(image.Rgb.Length == image.Width * image.Height * 3,
        "the portable decoder returned an invalid RGB buffer");
    }

    private static void PortableWebHostServesOperatorApi() {
      ParameterRegistry desktopRegistry =
        global::Spectrum.Web.SpectrumParameters.BuildRegistry(
          nativeWindowControlsAvailable: true);
      Assert(desktopRegistry.TryGet("vjHUDEnabled", out _) &&
          desktopRegistry.TryGet("domeSimulationEnabled", out _),
        "the Windows web registry lost its native-window controls");

      int port;
      using (var reservation = new TcpListener(IPAddress.Loopback, 0)) {
        reservation.Start();
        port = ((IPEndPoint)reservation.LocalEndpoint).Port;
      }

      using var dispatcher =
        new DedicatedThreadApplicationStateDispatcher();
      var config = new global::Spectrum.SpectrumConfiguration {
        webDomeSimulatorEnabled = false,
        vjHUDEnabled = true,
        domeSimulationEnabled = true,
      };
      dispatcher.InvokeAsync(
        () => config.AttachMutationDispatcher(dispatcher))
        .GetAwaiter().GetResult();
      var runtime = new global::Spectrum.Operator(
        config,
        dispatcher,
        new DisabledSpectrumInputFactory(),
        connectHardware: false);
      var web = new SpectrumWebHost(
        config, dispatcher, runtime, port,
        nativeWindowControlsAvailable: false);
      try {
        web.Start();
        using var client = new HttpClient {
          Timeout = TimeSpan.FromSeconds(5),
        };
        string response = client.GetStringAsync(
          "http://127.0.0.1:" + port + "/api/operator")
          .GetAwaiter().GetResult();
        using (JsonDocument document = JsonDocument.Parse(response)) {
          Assert(document.RootElement.TryGetProperty(
              "enabled", out JsonElement enabled) &&
              enabled.ValueKind == JsonValueKind.False,
            "the operator API returned an unexpected response: " + response);
        }
        string audioResponse = client.GetStringAsync(
          "http://127.0.0.1:" + port + "/api/maintenance/audio")
          .GetAwaiter().GetResult();
        using (JsonDocument document = JsonDocument.Parse(audioResponse)) {
          JsonElement root = document.RootElement;
          Assert(root.TryGetProperty("backend", out JsonElement backend) &&
              backend.GetString() == "Disabled" &&
              root.TryGetProperty(
                "availableDevices", out JsonElement availableDevices) &&
              availableDevices.ValueKind == JsonValueKind.Array &&
              availableDevices.GetArrayLength() == 0,
            "the audio setup API returned an unexpected response: " +
            audioResponse);
        }
        string runtimeResponse = client.GetStringAsync(
          "http://127.0.0.1:" + port + "/api/maintenance/runtime")
          .GetAwaiter().GetResult();
        using (JsonDocument document = JsonDocument.Parse(runtimeResponse)) {
          JsonElement root = document.RootElement;
          Assert(root.TryGetProperty("enabled", out JsonElement enabled) &&
              enabled.ValueKind == JsonValueKind.False &&
              root.TryGetProperty(
                "operatorFps", out JsonElement operatorFps) &&
              operatorFps.GetInt32() == 0 &&
              root.TryGetProperty("domeOpcFps", out JsonElement domeOpcFps) &&
              domeOpcFps.GetInt32() == 0 &&
              root.TryGetProperty(
                "layerPlanError", out JsonElement layerPlanError) &&
              layerPlanError.ValueKind == JsonValueKind.Null,
            "the runtime health API returned an unexpected response: " +
            runtimeResponse);
        }
        string parametersResponse = client.GetStringAsync(
          "http://127.0.0.1:" + port +
            "/api/maintenance/parameters")
          .GetAwaiter().GetResult();
        using (JsonDocument document = JsonDocument.Parse(parametersResponse)) {
          Assert(document.RootElement.ValueKind == JsonValueKind.Array,
            "the maintenance parameter API did not return an array");
          foreach (JsonElement parameter in
              document.RootElement.EnumerateArray()) {
            Assert(parameter.GetProperty("key").GetString() != "vjHUDEnabled" &&
                parameter.GetProperty("key").GetString() !=
                  "domeSimulationEnabled",
              "the headless API exposed native WPF window controls: " +
              parametersResponse);
          }
        }
        using var nativeWindowWrite = new StringContent(
          "{\"value\":false}", Encoding.UTF8, "application/json");
        HttpResponseMessage nativeWindowWriteResponse = client.PutAsync(
          "http://127.0.0.1:" + port +
            "/api/maintenance/parameters/vjHUDEnabled",
          nativeWindowWrite).GetAwaiter().GetResult();
        Assert(nativeWindowWriteResponse.StatusCode == HttpStatusCode.NotFound &&
            config.vjHUDEnabled,
          "the headless API accepted a native WPF window setting");
        using var audioWrite = new StringContent(
          "{\"value\":\"hw:test,0\"}",
          Encoding.UTF8,
          "application/json");
        HttpResponseMessage audioWriteResponse = client.PutAsync(
          "http://127.0.0.1:" + port +
            "/api/maintenance/parameters/audioDeviceID",
          audioWrite).GetAwaiter().GetResult();
        Assert(audioWriteResponse.IsSuccessStatusCode &&
            config.audioDeviceID == "hw:test,0",
          "the browser audio selection did not reach configuration");

        using (var eventsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "http://127.0.0.1:" + port + "/api/events"))
        using (HttpResponseMessage eventsResponse = client.SendAsync(
            eventsRequest, HttpCompletionOption.ResponseHeadersRead)
            .GetAwaiter().GetResult())
        using (Stream eventsBody = eventsResponse.Content.ReadAsStream())
        using (var eventsReader = new StreamReader(eventsBody)) {
          Assert(eventsResponse.IsSuccessStatusCode &&
              eventsResponse.Content.Headers.ContentType?.MediaType ==
                "text/event-stream",
            "the change-feed endpoint did not open an SSE response");
          using var flashWrite = new StringContent(
            "{\"value\":0.375}", Encoding.UTF8, "application/json");
          HttpResponseMessage flashWriteResponse = client.PutAsync(
            "http://127.0.0.1:" + port +
              "/api/parameters/flashSpeed",
            flashWrite).GetAwaiter().GetResult();
          Assert(flashWriteResponse.IsSuccessStatusCode,
            "the user parameter write failed while SSE was connected");

          bool sawFlashEvent = false;
          for (int line = 0; line < 64 && !sawFlashEvent; line++) {
            string? eventLine = eventsReader.ReadLineAsync()
              .WaitAsync(TimeSpan.FromSeconds(2))
              .GetAwaiter().GetResult();
            Assert(eventLine != null,
              "the SSE response ended before publishing the parameter write");
            if (!eventLine.StartsWith("data: ")) {
              continue;
            }
            using JsonDocument document = JsonDocument.Parse(
              eventLine.Substring("data: ".Length));
            JsonElement root = document.RootElement;
            sawFlashEvent =
              root.TryGetProperty("kind", out JsonElement kind) &&
              kind.GetString() == "param" &&
              root.TryGetProperty("key", out JsonElement key) &&
              key.GetString() == "flashSpeed" &&
              root.TryGetProperty("value", out JsonElement value) &&
              value.GetDouble() == 0.375;
          }
          Assert(sawFlashEvent,
            "the real SSE endpoint did not publish the parameter write");
        }
      } finally {
        web.StopAsync().GetAwaiter().GetResult();
      }
    }

    private static void ConfigurationSessionPersistence() {
      string directory = Path.Combine(
        Path.GetTempPath(), "spectrum-session-" + Guid.NewGuid());
      Directory.CreateDirectory(directory);
      try {
        string primary = Path.Combine(directory, "config.txt");
        string backup = Path.Combine(directory, "config.old.txt");
        string defaults = Path.Combine(directory, "config.default.txt");
        File.WriteAllText(defaults, "packaged");
        int saveCount = 0;
        int saveThread = 0;
        using var saved = new ManualResetEventSlim(false);
        var store = new ConfigurationFileStore<
          global::Spectrum.SpectrumConfiguration>(
            primary, backup, defaults,
            (stream, value) => {
              saveThread = Environment.CurrentManagedThreadId;
              Interlocked.Increment(ref saveCount);
              using var writer = new StreamWriter(
                stream, Encoding.UTF8, 1024, leaveOpen: true);
              writer.Write(value.domeBeagleboneOPCAddress);
              writer.Flush();
              saved.Set();
            },
            stream => {
              using var reader = new StreamReader(
                stream, Encoding.UTF8, true, 1024, leaveOpen: true);
              return new global::Spectrum.SpectrumConfiguration {
                domeBeagleboneOPCAddress = reader.ReadToEnd(),
              };
            });

        using var dispatcher =
          new DedicatedThreadApplicationStateDispatcher();
        var session = new global::Spectrum.SpectrumConfigurationSession(
          store, dispatcher, TimeSpan.FromMilliseconds(40));
        Assert(session.Configuration.domeBeagleboneOPCAddress == "packaged" &&
            session.LoadResult.SourcePath == defaults,
          "the configuration session did not load the packaged default");

        dispatcher.InvokeAsync(() => {
          session.Configuration.domeBeagleboneOPCAddress = "first";
          session.Configuration.domeBeagleboneOPCAddress = "second";
          session.Configuration.domeBeagleboneOPCAddress = "debounced";
        }).GetAwaiter().GetResult();
        Assert(saved.Wait(TimeSpan.FromSeconds(2)),
          "the debounced configuration save did not run");
        Assert(Volatile.Read(ref saveCount) == 1,
          "one change burst produced more than one configuration save");
        Assert(saveThread == dispatcher.InvokeAsync(
              () => Environment.CurrentManagedThreadId)
            .GetAwaiter().GetResult(),
          "configuration serialization ran away from the state-owner thread");

        saved.Reset();
        dispatcher.InvokeAsync(() =>
          session.Configuration.domeBeagleboneOPCAddress = "shutdown")
          .GetAwaiter().GetResult();
        session.Dispose();
        Assert(saved.IsSet && Volatile.Read(ref saveCount) == 2,
          "session shutdown did not flush exactly one pending save");
        Assert(File.ReadAllText(primary).Contains("shutdown"),
          "the shutdown flush did not persist the latest generation");
      } finally {
        Directory.Delete(directory, recursive: true);
      }
    }

    private sealed class FakeHostRuntime :
      global::Spectrum.ISpectrumHostRuntime {
      private readonly List<string> lifecycle;
      private bool enabled = true;

      public FakeHostRuntime(List<string> lifecycle) {
        this.lifecycle = lifecycle;
      }

      public int RebootCount { get; private set; }

      public bool Enabled {
        get => this.enabled;
        set {
          if (this.enabled == value) {
            return;
          }
          this.enabled = value;
          this.lifecycle.Add(value ? "runtime-start" : "runtime-stop");
        }
      }

      public void Reboot() {
        this.RebootCount++;
        this.lifecycle.Add("runtime-reboot");
      }
    }

    private sealed class FakeAlsaApi : IAlsaApi {
      public int OpenCount { get; private set; }
      public bool FailOpen { get; set; }
      public ManualResetEventSlim FrameProcessed { get; } = new();
      public ManualResetEventSlim RepeatedOpenFailure { get; } = new();

      public IReadOnlyList<AudioCaptureDevice> GetCaptureDevices() =>
        new[] { new AudioCaptureDevice("hw:test,0", "Test capture") };

      public IAlsaCapture OpenCapture(
        string deviceId,
        int sampleRate,
        int framesPerRead
      ) {
        Assert(deviceId == "hw:test,0" && sampleRate == 44100,
          "the ALSA worker opened the wrong device or sample rate");
        this.OpenCount++;
        if (this.FailOpen) {
          if (this.OpenCount >= 3) {
            this.RepeatedOpenFailure.Set();
          }
          throw new InvalidOperationException("test device unavailable");
        }
        return new FakeAlsaCapture(this.FrameProcessed);
      }
    }

    private sealed class FakeAlsaCapture : IAlsaCapture {
      private readonly ManualResetEventSlim frameProcessed;
      private int readCount;

      public FakeAlsaCapture(ManualResetEventSlim frameProcessed) {
        this.frameProcessed = frameProcessed;
      }

      public int Channels => 2;

      public int Read(short[] samples) {
        if (Interlocked.Increment(ref this.readCount) == 2) {
          this.frameProcessed.Set();
        }
        Array.Clear(samples, 0, samples.Length);
        samples[0] = short.MinValue;
        Thread.Yield();
        return samples.Length;
      }

      public void Dispose() { }
    }

    private sealed class FakePcmBeatTracker : IPcmBeatTracker {
      private int enabled;
      private int writeCount;
      private int lastChannels;
      private int lastSampleCount;
      public ManualResetEventSlim Written { get; } = new();
      public ManualResetEventSlim Disabled { get; } = new();

      public bool Enabled {
        get => Volatile.Read(ref this.enabled) != 0;
        set {
          int previous = Interlocked.Exchange(
            ref this.enabled, value ? 1 : 0);
          if (!value && previous != 0) {
            this.Disabled.Set();
          }
        }
      }

      public string? LastError => this.LastErrorValue;
      public string? LastErrorValue { get; set; }
      public int WriteCount => Volatile.Read(ref this.writeCount);
      public int LastChannels => Volatile.Read(ref this.lastChannels);
      public int LastSampleCount => Volatile.Read(ref this.lastSampleCount);

      public void Write(
        short[] samples,
        int sampleCount,
        int channels
      ) {
        if (!this.Enabled) {
          return;
        }
        Volatile.Write(ref this.lastChannels, channels);
        Volatile.Write(ref this.lastSampleCount, sampleCount);
        Interlocked.Increment(ref this.writeCount);
        this.Written.Set();
      }

      public void Dispose() => this.Enabled = false;
    }

    private sealed class FakeHostService :
      global::Spectrum.ISpectrumHostService {
      private readonly List<string> lifecycle;

      public FakeHostService(List<string> lifecycle) {
        this.lifecycle = lifecycle;
      }

      public void Start() => this.lifecycle.Add("service-start");

      public async Task StopAsync() {
        await Task.Delay(10).ConfigureAwait(false);
        this.lifecycle.Add("service-stop");
      }
    }

    private static void PortableHostLifecycle() {
      string directory = Path.Combine(
        Path.GetTempPath(), "spectrum-host-" + Guid.NewGuid());
      Directory.CreateDirectory(directory);
      try {
        string primary = Path.Combine(directory, "config.txt");
        string backup = Path.Combine(directory, "config.old.txt");
        string defaults = Path.Combine(directory, "config.default.txt");
        File.WriteAllText(defaults, "packaged");
        var lifecycle = new List<string>();
        var store = new ConfigurationFileStore<
          global::Spectrum.SpectrumConfiguration>(
            primary, backup, defaults,
            (stream, value) => {
              lifecycle.Add("configuration-save");
              using var writer = new StreamWriter(
                stream, Encoding.UTF8, 1024, leaveOpen: true);
              writer.Write(value.domeBeagleboneOPCAddress);
              writer.Flush();
            },
            stream => {
              using var reader = new StreamReader(
                stream, Encoding.UTF8, true, 1024, leaveOpen: true);
              return new global::Spectrum.SpectrumConfiguration {
                domeBeagleboneOPCAddress = reader.ReadToEnd(),
              };
            });

        using var dispatcher =
          new DedicatedThreadApplicationStateDispatcher();
        FakeHostRuntime? runtime = null;
        var host = new global::Spectrum.SpectrumHost<
          FakeHostRuntime, FakeHostService>(
            store,
            dispatcher,
            TimeSpan.FromSeconds(30),
            (config, owner) => runtime = new FakeHostRuntime(lifecycle),
            (config, owner, engine) => new FakeHostService(lifecycle),
            new[] {
              nameof(global::Spectrum.SpectrumConfiguration
                .domeOutputInSeparateThread),
            });

        Assert(runtime != null,
          "the host did not construct its runtime");

        Assert(host.Configuration.domeBeagleboneOPCAddress == "packaged",
          "the host did not expose the loaded configuration");
        host.Start();
        dispatcher.InvokeAsync(() => {
          host.Configuration.domeBeagleboneOPCAddress = "shutdown";
          host.Configuration.domeOutputInSeparateThread =
            !host.Configuration.domeOutputInSeparateThread;
        }).GetAwaiter().GetResult();
        Assert(runtime.RebootCount == 1,
          "the host did not apply its reboot-on-setting policy");

        host.Dispose();
        Assert(host.ServiceStartError == null,
          "a successful host service reported a startup failure");
        Assert(!runtime.Enabled,
          "host shutdown left the runtime enabled");
        Assert(File.ReadAllText(primary).Contains("shutdown"),
          "host shutdown did not flush the latest configuration");
        Assert(lifecycle.IndexOf("service-stop") >= 0 &&
            lifecycle.IndexOf("runtime-stop") >
              lifecycle.IndexOf("service-stop") &&
            lifecycle.IndexOf("configuration-save") >
              lifecycle.IndexOf("runtime-stop"),
          "host shutdown did not stop service, runtime, then persistence");
      } finally {
        Directory.Delete(directory, recursive: true);
      }
    }

    private static void HeadlessConfigurationPaths() {
      string root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath())) ??
        throw new InvalidOperationException(
          "the temporary directory has no filesystem root");
      string explicitDirectory = Path.Combine(root, "explicit-spectrum");
      string environmentDirectory = Path.Combine(root, "environment-spectrum");
      string xdgDirectory = Path.Combine(root, "xdg-config");
      string homeDirectory = Path.Combine(root, "home", "operator");
      string packagedDefault = Path.Combine(root, "app", "default.xml");

      global::Spectrum.SpectrumConfigurationPaths explicitPaths =
        global::Spectrum.SpectrumConfigurationPaths.ForHeadlessHost(
          packagedDefault,
          explicitDirectory,
          name => name == "SPECTRUM_DATA_DIR"
            ? environmentDirectory
            : name == "XDG_CONFIG_HOME" ? xdgDirectory : null,
          homeDirectory,
          Path.Combine(root, "local-app-data"),
          isWindows: false);
      Assert(explicitPaths.PrimaryPath == Path.Combine(
            explicitDirectory, "spectrum_config.xml") &&
          explicitPaths.DefaultPath == packagedDefault,
        "the explicit headless data directory did not take precedence");

      global::Spectrum.SpectrumConfigurationPaths environmentPaths =
        global::Spectrum.SpectrumConfigurationPaths.ForHeadlessHost(
          packagedDefault,
          null,
          name => name == "SPECTRUM_DATA_DIR"
            ? environmentDirectory
            : name == "XDG_CONFIG_HOME" ? xdgDirectory : null,
          homeDirectory,
          Path.Combine(root, "local-app-data"),
          isWindows: false);
      Assert(environmentPaths.BackupPath == Path.Combine(
            environmentDirectory, "spectrum_old_config.xml"),
        "SPECTRUM_DATA_DIR did not override XDG_CONFIG_HOME");

      global::Spectrum.SpectrumConfigurationPaths xdgPaths =
        global::Spectrum.SpectrumConfigurationPaths.ForHeadlessHost(
          packagedDefault,
          null,
          name => name == "XDG_CONFIG_HOME" ? xdgDirectory : null,
          homeDirectory,
          Path.Combine(root, "local-app-data"),
          isWindows: false);
      Assert(xdgPaths.PrimaryPath == Path.Combine(
            xdgDirectory, "spectrum", "spectrum_config.xml"),
        "the Linux host did not use XDG_CONFIG_HOME");

      global::Spectrum.SpectrumConfigurationPaths fallbackPaths =
        global::Spectrum.SpectrumConfigurationPaths.ForHeadlessHost(
          packagedDefault,
          null,
          _ => null,
          homeDirectory,
          Path.Combine(root, "local-app-data"),
          isWindows: false);
      Assert(fallbackPaths.PrimaryPath == Path.Combine(
            homeDirectory, ".config", "spectrum", "spectrum_config.xml"),
        "the Linux host did not fall back to ~/.config/spectrum");
    }

    private static void LinuxSerialPortDiscovery() {
      string byIdWand = "/dev/serial/by-id/usb-spectrum-wand";
      string byPathWand = "/dev/serial/by-path/pci-wand";
      string byPathBridge = "/dev/serial/by-path/pci-bridge";
      string[] ports = WandSerialReceiver.MergeLinuxPortNames(
        new[] {
          "/dev/ttyUSB0",
          "/dev/ttyACM0",
          "/dev/ttyUSB0",
          "",
        },
        new KeyValuePair<string, string?>[] {
          new KeyValuePair<string, string?>(byIdWand, "/dev/ttyACM0"),
          new KeyValuePair<string, string?>(byPathWand, "/dev/ttyACM0"),
          new KeyValuePair<string, string?>(byPathBridge, "/dev/ttyUSB1"),
        });

      string[] expected = {
        byIdWand,
        byPathBridge,
        "/dev/ttyUSB0",
      };
      Assert(ports.AsSpan().SequenceEqual(expected),
        "stable aliases did not replace transient or duplicate Linux ports");
    }

    private static void OpcWireFrame() {
      byte[] partialPayload = {
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06,
      };
      var partialSender = new PartialOpcSender(2);
      OPCAPI.SendAll(
        partialPayload, partialPayload.Length, partialSender);
      Assert(partialSender.Calls >= 4 &&
          partialSender.Bytes.AsSpan().SequenceEqual(partialPayload),
        "the OPC send loop truncated a sequence of partial writes");

      int port;
      using (var reservation = new TcpListener(IPAddress.Loopback, 0)) {
        reservation.Start();
        port = ((IPEndPoint)reservation.LocalEndpoint).Port;
      }

      var opc = new OPCAPI(
        "127.0.0.1:" + port + ":7", false, _ => { },
        TimeSpan.Zero);
      try {
        CompleteWithoutBlocking(
          () => opc.Active = true,
          "an offline OPC controller blocked output activation");

        opc.SetPixel(0, 0x123456);
        opc.SetPixel(2, 0xABCDEF);
        opc.Flush();

        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Task<Socket> accept = listener.AcceptSocketAsync();
        WaitHandle? pendingConnect = opc.PendingConnectWaitHandle;
        Assert(pendingConnect != null &&
            pendingConnect.WaitOne(TimeSpan.FromSeconds(2)),
          "the OPC connection did not complete after the controller started");
        using Socket connection = accept
          .WaitAsync(TimeSpan.FromSeconds(2))
          .GetAwaiter().GetResult();
        connection.ReceiveTimeout = 2000;
        CompleteWithoutBlocking(
          opc.OperatorUpdate,
          "publishing the pending OPC frame blocked the operator");
        byte[] actual = Receive(connection, 13);
        byte[] expected = {
          7, 0, 0, 9,
          0x12, 0x34, 0x56,
          0x00, 0x00, 0x00,
          0xAB, 0xCD, 0xEF,
        };
        Assert(actual.AsSpan().SequenceEqual(expected),
          "the OPC header or RGB payload changed");
      } finally {
        opc.Active = false;
      }
    }

    private sealed class PartialOpcSender : OPCAPI.IOpcByteSender {
      private readonly int maxChunk;
      private readonly List<byte> bytes = new List<byte>();

      public PartialOpcSender(int maxChunk) {
        this.maxChunk = maxChunk;
      }

      public int Calls { get; private set; }
      public byte[] Bytes => this.bytes.ToArray();

      public int Send(byte[] buffer, int offset, int count) {
        int written = Math.Min(this.maxChunk, count);
        for (int i = 0; i < written; i++) {
          this.bytes.Add(buffer[offset + i]);
        }
        this.Calls++;
        return written;
      }
    }

    private static void CompleteWithoutBlocking(Action action, string message) {
      try {
        Task.Run(action)
          .WaitAsync(TimeSpan.FromSeconds(2))
          .GetAwaiter().GetResult();
      } catch (TimeoutException) {
        throw new InvalidOperationException(message);
      }
    }

    private static byte[] Receive(Socket socket, int length) {
      var bytes = new byte[length];
      int received = 0;
      while (received < length) {
        int count = socket.Receive(
          bytes, received, length - received, SocketFlags.None);
        if (count == 0) {
          throw new InvalidOperationException(
            "the OPC connection closed before the frame completed");
        }
        received += count;
      }
      return bytes;
    }

  }
}

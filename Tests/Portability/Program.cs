using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using Spectrum.Web;

namespace Spectrum.Portability.Tests {

  internal static class Program {
    private static int failures;

    private static int Main() {
      Run("headless state dispatcher owns one dedicated thread",
        DedicatedThreadOwnership);
      Run("headless state dispatcher serializes concurrent commands",
        DedicatedThreadSerialization);
      Run("headless state dispatcher contains command failures",
        DedicatedThreadFailureContainment);
      Run("headless state dispatcher drains accepted commands on shutdown",
        DedicatedThreadShutdown);
      Run("portable configuration store saves and recovers atomically",
        ConfigurationStoreRecovery);
      Run("portable configuration session debounces and flushes saves",
        ConfigurationSessionPersistence);
      Run("portable host owns runtime, service, reboot, and shutdown lifecycle",
        PortableHostLifecycle);
      Run("headless configuration paths follow explicit and XDG policy",
        HeadlessConfigurationPaths);
      Run("Madmom runtime discovery supports Windows and Unix layouts",
        MadmomRuntimeDiscovery);
      Run("portable core compiles built-in layer configuration",
        PortableCoreLayerConfiguration);
      Run("portable runtime assembly has no Windows desktop dependencies",
        PortableRuntimeAssemblyBoundary);
      Run("portable Earth texture decoder reads the embedded asset",
        PortableEarthTextureDecoder);
      Run("portable web host serves the operator API",
        PortableWebHostServesOperatorApi);
      Run("portable OPC client emits the expected wire frame", OpcWireFrame);
      return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test) {
      try {
        test();
        Console.WriteLine("PASS " + name);
      } catch (Exception error) {
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + error);
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

        MadmomRuntimePaths windows = MadmomRuntimeLocator.Find(
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

        MadmomRuntimePaths unix = MadmomRuntimeLocator.Find(
          nested, useWindowsLayout: false);
        Assert(unix != null &&
            unix.PythonPath == unixPython &&
            unix.TrackerPath == unixTracker,
          "the packaged Unix runtime layout was not found");

        MadmomRuntimePaths missing = MadmomRuntimeLocator.Find(
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
      LayerDefinition definition =
        global::Spectrum.BuiltInDomeLayerCatalog.Metadata.Get("radial");
      Assert(definition.CompileOptions(
            snapshot.Layers[0].RendererParameters)
          is RadialLayerOptions options && options.Size == 0.25,
        "the portable built-in catalog did not compile typed layer options");
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
        Assert(!forbidden.Contains(reference.Name),
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
      using Stream texture = typeof(LEDDomeEarthVisualizer).Assembly
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
      int port;
      using (var reservation = new TcpListener(IPAddress.Loopback, 0)) {
        reservation.Start();
        port = ((IPEndPoint)reservation.LocalEndpoint).Port;
      }

      using var dispatcher =
        new DedicatedThreadApplicationStateDispatcher();
      var config = new global::Spectrum.SpectrumConfiguration {
        webDomeSimulatorEnabled = false,
      };
      dispatcher.InvokeAsync(
        () => config.AttachMutationDispatcher(dispatcher))
        .GetAwaiter().GetResult();
      var runtime = new global::Spectrum.Operator(
        config,
        dispatcher,
        new DisabledSpectrumInputFactory(),
        connectHardware: false);
      var web = new SpectrumWebHost(config, dispatcher, runtime, port);
      try {
        web.Start();
        using var client = new HttpClient {
          Timeout = TimeSpan.FromSeconds(5),
        };
        string response = client.GetStringAsync(
          "http://127.0.0.1:" + port + "/api/operator")
          .GetAwaiter().GetResult();
        Assert(response.Contains("\"enabled\":false"),
          "the operator API returned an unexpected response: " + response);
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
        FakeHostRuntime runtime = null;
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
      string root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()));
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

    private static void OpcWireFrame() {
      using var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      int port = ((IPEndPoint)listener.LocalEndpoint).Port;
      Task<Socket> accept = listener.AcceptSocketAsync();
      var opc = new OPCAPI("127.0.0.1:" + port + ":7", false, _ => { });
      try {
        opc.Active = true;
        using Socket connection = accept.WaitAsync(TimeSpan.FromSeconds(2))
          .GetAwaiter().GetResult();
        connection.ReceiveTimeout = 2000;
        opc.SetPixel(0, 0x123456);
        opc.SetPixel(2, 0xABCDEF);
        opc.Flush();
        for (int attempt = 0; attempt < 100 && connection.Available == 0;
            attempt++) {
          Thread.Sleep(2);
          opc.OperatorUpdate();
        }

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

    private static void Assert(bool condition, string message) {
      if (!condition) {
        throw new InvalidOperationException(message);
      }
    }
  }
}

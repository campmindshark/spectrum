using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.Web;
using XSerializer;

namespace Spectrum.Host {

  internal static class Program {
    private const int DefaultWebPort = 8080;

    private static async Task<int> Main(string[] args) {
      if (!HostOptions.TryParse(args, out HostOptions options, out string error)) {
        Console.Error.WriteLine(error);
        Console.Error.WriteLine(HostOptions.Usage);
        return 2;
      }
      if (options.Help) {
        Console.WriteLine(HostOptions.Usage);
        return 0;
      }

      string packagedDefault = Path.Combine(
        AppContext.BaseDirectory, "spectrum_default_config.xml");
      SpectrumConfigurationPaths paths =
        SpectrumConfigurationPaths.ForHeadlessHost(
          packagedDefault, options.DataDirectory);
      Directory.CreateDirectory(
        Path.GetDirectoryName(paths.PrimaryPath) ??
          throw new InvalidOperationException(
            "The configuration directory could not be resolved."));

      var store = new ConfigurationFileStore<SpectrumConfiguration>(
        paths.PrimaryPath,
        paths.BackupPath,
        paths.DefaultPath,
        (stream, value) =>
          new XmlSerializer<SpectrumConfigurationDocument>().Serialize(
            stream,
            SpectrumConfigurationDocument.FromConfiguration(value)),
        stream => new XmlSerializer<SpectrumConfigurationDocument>()
          .Deserialize(stream).ToConfiguration());

      try {
        using var dispatcher =
          new DedicatedThreadApplicationStateDispatcher(
            reportUnhandledException: exception =>
              Console.Error.WriteLine(
                "State-owner command failed: " + exception));
        await using var host = new SpectrumHost<Operator, SpectrumWebHost>(
          store,
          dispatcher,
          TimeSpan.FromMilliseconds(100),
          (config, owner) => new Operator(
            config, owner, new DisabledSpectrumInputFactory()),
          (config, owner, runtime) => new SpectrumWebHost(
            config, owner, runtime, options.WebPort),
          new[] { nameof(SpectrumConfiguration.domeOutputInSeparateThread) },
          reportLoadFailure: failure => Console.Error.WriteLine(
            "Could not load " + failure.Path + ": " +
            failure.Error.Message),
          reportSaveError: exception => Console.Error.WriteLine(
            "Could not save configuration: " + exception),
          reportServiceStartError: exception => Console.Error.WriteLine(
            "Web controller failed to start: " + exception.Message));

        if (options.CheckOnly) {
          Console.WriteLine(
            "Headless host check passed; configuration source: " +
            (host.LoadResult.SourcePath ?? "built-in empty configuration"));
          return 0;
        }

        host.Start();
        if (host.ServiceStartError != null) {
          return 1;
        }
        host.Runtime.Enabled = true;
        Console.WriteLine("Spectrum headless host is running.");
        Console.WriteLine("Controller: http://0.0.0.0:" + options.WebPort);
        Console.WriteLine("Configuration: " + paths.PrimaryPath);
        Console.WriteLine("Press Ctrl+C to stop.");

        var shutdown = new TaskCompletionSource(
          TaskCreationOptions.RunContinuationsAsynchronously);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) => {
          eventArgs.Cancel = true;
          shutdown.TrySetResult();
        };
        Console.CancelKeyPress += cancelHandler;
        PosixSignalRegistration terminate = null;
        if (!OperatingSystem.IsWindows()) {
          terminate = PosixSignalRegistration.Create(
            PosixSignal.SIGTERM,
            context => {
              context.Cancel = true;
              shutdown.TrySetResult();
            });
        }
        try {
          await shutdown.Task.ConfigureAwait(false);
        } finally {
          terminate?.Dispose();
          Console.CancelKeyPress -= cancelHandler;
        }
        Console.WriteLine("Stopping Spectrum headless host...");
        return 0;
      } catch (Exception exception) {
        Console.Error.WriteLine("Spectrum headless host failed: " + exception);
        return 1;
      }
    }

    private sealed record HostOptions(
      string DataDirectory,
      int WebPort,
      bool CheckOnly,
      bool Help
    ) {
      public const string Usage =
        "Usage: Spectrum.Host [--data-dir PATH] [--port PORT] [--check]\n" +
        "\n" +
        "Runs the browser-controlled Spectrum engine with audio and MIDI " +
        "disabled.\n" +
        "Configuration defaults to SPECTRUM_DATA_DIR, XDG_CONFIG_HOME/" +
        "spectrum,\n" +
        "or ~/.config/spectrum. --check validates composition without " +
        "starting services.";

      public static bool TryParse(
        string[] args,
        out HostOptions options,
        out string error
      ) {
        string dataDirectory = null;
        int port = DefaultWebPort;
        bool checkOnly = false;
        bool help = false;
        for (int i = 0; i < args.Length; i++) {
          switch (args[i]) {
            case "--data-dir":
              if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i])) {
                options = null;
                error = "--data-dir requires a path.";
                return false;
              }
              dataDirectory = args[i];
              break;
            case "--port":
              if (++i >= args.Length || !int.TryParse(args[i], out port) ||
                  port < 1 || port > 65535) {
                options = null;
                error = "--port requires an integer from 1 through 65535.";
                return false;
              }
              break;
            case "--check":
              checkOnly = true;
              break;
            case "--help":
            case "-h":
              help = true;
              break;
            default:
              options = null;
              error = "Unknown argument: " + args[i];
              return false;
          }
        }

        options = new HostOptions(dataDirectory, port, checkOnly, help);
        error = null;
        return true;
      }
    }
  }
}

using System;
using System.Threading;

namespace Spectrum.LEDs {

  // Renderer constructors register themselves with their Output. This narrow
  // construction scope carries the instance identity through that existing
  // registration call without coupling visualizer implementations to output.
  public static class LayerInstanceScope {
    private static readonly AsyncLocal<string> current = new();
    public static string CurrentId => current.Value;

    public static IDisposable Push(string instanceId) {
      string previous = current.Value;
      current.Value = instanceId;
      return new PopScope(previous);
    }

    private sealed class PopScope : IDisposable {
      private readonly string previous;
      public PopScope(string previous) { this.previous = previous; }
      public void Dispose() { current.Value = this.previous; }
    }
  }
}

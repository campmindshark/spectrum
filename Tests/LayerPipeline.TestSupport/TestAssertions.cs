using System;
using System.Diagnostics.CodeAnalysis;

namespace Spectrum.LayerPipeline.Tests {

  public static class TestAssertions {
    public static void Assert(
      [DoesNotReturnIf(false)] bool condition, string? message
    ) {
      if (!condition) {
        throw new InvalidOperationException(message);
      }
    }
  }
}

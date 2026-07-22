using System;
using System.Collections.Generic;

namespace Spectrum {

  /**
   * Canonical labels for the integer domeTestPattern setting. The position of
   * each label is the persisted/runtime value, so every control surface must
   * use this list rather than maintaining its own copy.
   */
  public static class DomeTestPatterns {

    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(
      new[] {
        "None",
        "Flash Colors By Strut",
        "Iterate Through Struts",
        "Strip Test",
        "Full Color Flash",
        "Quaternion Test",
      });

  }
}

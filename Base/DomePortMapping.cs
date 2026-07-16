using System.Collections.Generic;

namespace Spectrum.Base {

  // Serialization DTO for one dome-side box's physical-port -> legacy-path
  // permutation. The list is intentionally null by default so XSerializer can
  // populate it without mutating a preinitialized collection.
  public sealed class DomePortMapping {
    public List<int> ports { get; set; }

    public DomePortMapping() {
    }

    public DomePortMapping(IEnumerable<int> ports) {
      this.ports = ports == null ? null : new List<int>(ports);
    }
  }
}

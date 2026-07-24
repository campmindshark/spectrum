using System;
using System.Collections.Generic;

namespace Spectrum {

  internal readonly record struct FailureUpdate(
    int ConsecutiveFailures,
    bool NewlyQuarantined
  );

  /// <summary>
  /// Tracks consecutive failures for long-lived runtime components. Successful
  /// work clears a transient streak; a quarantined component remains isolated
  /// until the owner explicitly resets or forgets it.
  /// </summary>
  internal sealed class ConsecutiveFailureTracker<T> where T : notnull {
    private readonly int quarantineThreshold;
    private readonly Dictionary<T, int> consecutiveFailures = new();
    private readonly HashSet<T> quarantined = new();

    public ConsecutiveFailureTracker(int quarantineThreshold) {
      if (quarantineThreshold < 1) {
        throw new ArgumentOutOfRangeException(
          nameof(quarantineThreshold),
          "The quarantine threshold must be positive.");
      }
      this.quarantineThreshold = quarantineThreshold;
    }

    public FailureUpdate RecordFailure(T component) {
      if (this.quarantined.Contains(component)) {
        return new FailureUpdate(this.quarantineThreshold, false);
      }

      this.consecutiveFailures.TryGetValue(component, out int count);
      count++;
      if (count >= this.quarantineThreshold) {
        this.consecutiveFailures.Remove(component);
        this.quarantined.Add(component);
        return new FailureUpdate(count, true);
      }

      this.consecutiveFailures[component] = count;
      return new FailureUpdate(count, false);
    }

    public void RecordSuccess(T component) {
      if (!this.quarantined.Contains(component)) {
        this.consecutiveFailures.Remove(component);
      }
    }

    public bool IsQuarantined(T component) =>
      this.quarantined.Contains(component);

    public void Forget(T component) {
      this.consecutiveFailures.Remove(component);
      this.quarantined.Remove(component);
    }

    public void Reset() {
      this.consecutiveFailures.Clear();
      this.quarantined.Clear();
    }
  }
}

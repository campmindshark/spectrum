using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Spectrum.Web {

  /**
   * Server-side advisory locks for modal / multi-step operations
   * (docs/web_architecture.md, problem 2). Calibration and the per-device test
   * patterns are exclusive diagnostic flows; two users entering one at once
   * corrupts the flow. A lease says "someone is calibrating the dome" and
   * blocks conflicting writes from anyone else.
   *
   * Leases are time-limited and renewed by heartbeat, so a phone that walks out
   * of range or crashes doesn't hold a device hostage forever.
   *
   * "Advisory" here means: a write to a modal resource is allowed when nobody
   * holds the lease, but denied while another holder's lease is active. The
   * maintenance UI is expected to acquire the lease before entering the flow.
   */
  public sealed class AdvisoryLockManager {

    public sealed class LockInfo {
      public string resource { get; set; }
      public string holderName { get; set; }
      public long expiresInMs { get; set; }
    }

    private sealed class Lease {
      public string HolderToken;
      public string HolderName;
      public DateTime ExpiresUtc;
    }

    private readonly ConcurrentDictionary<string, Lease> byResource =
      new ConcurrentDictionary<string, Lease>();
    private readonly TimeSpan ttl;
    private readonly object gate = new object();

    public AdvisoryLockManager(TimeSpan? ttl = null) {
      this.ttl = ttl ?? TimeSpan.FromSeconds(15);
    }

    private static bool IsActive(Lease lease, DateTime now) =>
      lease != null && lease.ExpiresUtc > now;

    // Acquire (or renew, if the caller already holds it) a lease. Returns the
    // holder token on success; null if another active holder has it.
    public string TryAcquire(string resource, string holderName, out LockInfo current) {
      lock (this.gate) {
        DateTime now = DateTime.UtcNow;
        this.byResource.TryGetValue(resource, out Lease existing);
        if (IsActive(existing, now)) {
          current = ToInfo(resource, existing, now);
          return null;
        }
        var lease = new Lease {
          HolderToken = Guid.NewGuid().ToString("N"),
          HolderName = holderName,
          ExpiresUtc = now + this.ttl,
        };
        this.byResource[resource] = lease;
        current = ToInfo(resource, lease, now);
        return lease.HolderToken;
      }
    }

    // Extend a lease the caller holds. False if they no longer hold it.
    public bool TryRenew(string resource, string holderToken) {
      lock (this.gate) {
        if (this.byResource.TryGetValue(resource, out Lease lease) &&
            IsActive(lease, DateTime.UtcNow) &&
            lease.HolderToken == holderToken) {
          lease.ExpiresUtc = DateTime.UtcNow + this.ttl;
          return true;
        }
        return false;
      }
    }

    public bool TryRelease(string resource, string holderToken) {
      lock (this.gate) {
        if (this.byResource.TryGetValue(resource, out Lease lease) &&
            lease.HolderToken == holderToken) {
          this.byResource.TryRemove(resource, out _);
          return true;
        }
        return false;
      }
    }

    // Whether a write to a modal resource is permitted for the given holder
    // token (may be null for a caller that holds no lease). Permitted when the
    // resource is unlocked or the caller is the active holder.
    public bool CanWrite(string resource, string holderToken) {
      lock (this.gate) {
        if (!this.byResource.TryGetValue(resource, out Lease lease) ||
            !IsActive(lease, DateTime.UtcNow)) {
          return true;
        }
        return lease.HolderToken == holderToken;
      }
    }

    // Whether the given token currently holds an active lease on the resource.
    // Unlike CanWrite this is false when the resource is unlocked: the modal
    // calibration flow requires an explicitly acquired lease, not merely the
    // absence of a competing one.
    public bool HoldsLock(string resource, string holderToken) {
      lock (this.gate) {
        return holderToken != null &&
          this.byResource.TryGetValue(resource, out Lease lease) &&
          IsActive(lease, DateTime.UtcNow) &&
          lease.HolderToken == holderToken;
      }
    }

    public LockInfo Get(string resource) {
      lock (this.gate) {
        DateTime now = DateTime.UtcNow;
        if (this.byResource.TryGetValue(resource, out Lease lease) &&
            IsActive(lease, now)) {
          return ToInfo(resource, lease, now);
        }
        return null;
      }
    }

    public List<LockInfo> ActiveLocks() {
      var result = new List<LockInfo>();
      lock (this.gate) {
        DateTime now = DateTime.UtcNow;
        foreach (KeyValuePair<string, Lease> kv in this.byResource) {
          if (IsActive(kv.Value, now)) {
            result.Add(ToInfo(kv.Key, kv.Value, now));
          }
        }
      }
      return result;
    }

    private static LockInfo ToInfo(string resource, Lease lease, DateTime now) =>
      new LockInfo {
        resource = resource,
        holderName = lease.HolderName,
        expiresInMs = (long)(lease.ExpiresUtc - now).TotalMilliseconds,
      };
  }

  /**
   * Maps a parameter key to the advisory-lock resource it participates in, or
   * null if the parameter is free of modal locking. Test patterns each get
   * their own per-device resource; the "domeCalibration" resource is reserved
   * for the calibration flow (driven through the lock endpoints).
   */
  public static class LockPolicy {

    public const string DomeCalibration = "domeCalibration";

    public static string ResourceForKey(string key) {
      switch (key) {
        case "domeTestPattern": return "domeTest";
        case "barTestPattern": return "barTest";
        case "stageTestPattern": return "stageTest";
        default: return null;
      }
    }
  }
}

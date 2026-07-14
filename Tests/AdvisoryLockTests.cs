using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Spectrum.LayerPipeline.Tests {

  internal static class AdvisoryLockTests {

    public static void Register(Action<string, Action> run) {
      run("advisory lock tokens govern writes and release", OwnershipPolicy);
      run("advisory locks admit one concurrent holder", ConcurrentAcquire);
    }

    private static void OwnershipPolicy() {
      var locks = new global::Spectrum.Web.AdvisoryLockManager(
        TimeSpan.FromMinutes(1));
      string resource = global::Spectrum.Web.LockPolicy.ResourceForKey(
        "domeTestPattern");
      Assert(resource == "domeTest", "test-pattern lock policy changed");

      string alice = locks.TryAcquire(resource, "Alice", out var acquired);
      Assert(!string.IsNullOrEmpty(alice), "first lock acquisition failed");
      Assert(acquired.resource == resource && acquired.holderName == "Alice",
        "acquisition returned the wrong owner");

      string bob = locks.TryAcquire(resource, "Bob", out var blocked);
      Assert(bob == null && blocked.holderName == "Alice",
        "competing holder replaced the active lease");
      Assert(locks.CanWrite(resource, alice),
        "active holder cannot write its resource");
      Assert(!locks.CanWrite(resource, null) &&
        !locks.CanWrite(resource, "not-a-token"),
        "caller without the lease can write a locked resource");
      Assert(locks.HoldsLock(resource, alice) &&
        !locks.HoldsLock(resource, null),
        "explicit ownership check disagrees with the lease");

      Assert(!locks.TryRenew(resource, "not-a-token"),
        "wrong token renewed the lease");
      Assert(locks.TryRenew(resource, alice),
        "holder could not renew the lease");
      Assert(!locks.TryRelease(resource, "not-a-token") &&
        locks.Get(resource)?.holderName == "Alice",
        "wrong token released the lease");
      Assert(locks.TryRelease(resource, alice),
        "holder could not release the lease");
      Assert(locks.Get(resource) == null && locks.CanWrite(resource, null),
        "released resource remained locked");
    }

    private static void ConcurrentAcquire() {
      const int contenders = 8;
      var locks = new global::Spectrum.Web.AdvisoryLockManager(
        TimeSpan.FromMinutes(1));
      using var ready = new CountdownEvent(contenders);
      using var start = new ManualResetEventSlim(false);
      var tokens = new ConcurrentBag<string>();
      var threads = new Thread[contenders];

      for (int i = 0; i < contenders; i++) {
        int contender = i;
        threads[i] = new Thread(() => {
          ready.Signal();
          start.Wait();
          string token = locks.TryAcquire(
            "domeCalibration", "client-" + contender, out _);
          if (token != null) {
            tokens.Add(token);
          }
        }) {
          IsBackground = true,
        };
        threads[i].Start();
      }

      Assert(ready.Wait(TimeSpan.FromSeconds(5)),
        "lock contenders did not become ready");
      start.Set();
      foreach (Thread thread in threads) {
        Assert(thread.Join(TimeSpan.FromSeconds(5)),
          "lock contender did not finish");
      }
      Assert(tokens.Count == 1,
        "concurrent acquisition produced " + tokens.Count + " holders");
      Assert(locks.ActiveLocks().Count == 1,
        "concurrent acquisition published multiple leases");
    }

    private static void Assert(bool condition, string message) {
      if (!condition) {
        throw new InvalidOperationException(message);
      }
    }
  }
}

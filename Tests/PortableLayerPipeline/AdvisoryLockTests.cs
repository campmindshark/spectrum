using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Spectrum.LayerPipeline.Tests {

  [TestClass]
  [DoNotParallelize]
  public sealed class AdvisoryLockTests {


    [TestMethod]
    public void OwnershipPolicy() {
      var locks = new global::Spectrum.Web.AdvisoryLockManager(
        TimeSpan.FromMinutes(1));
      string? resource = global::Spectrum.Web.LockPolicy.ResourceForKey(
        "domeTestPattern");
      Assert.IsTrue(resource == "domeTest", "test-pattern lock policy changed");

      string? alice = locks.TryAcquire(resource, "Alice", out var acquired);
      Assert.IsTrue(!string.IsNullOrEmpty(alice), "first lock acquisition failed");
      Assert.IsTrue(acquired.resource == resource && acquired.holderName == "Alice",
        "acquisition returned the wrong owner");

      string? bob = locks.TryAcquire(resource, "Bob", out var blocked);
      Assert.IsTrue(bob == null && blocked.holderName == "Alice",
        "competing holder replaced the active lease");
      Assert.IsTrue(locks.CanWrite(resource, alice),
        "active holder cannot write its resource");
      Assert.IsTrue(!locks.CanWrite(resource, null) &&
        !locks.CanWrite(resource, "not-a-token"),
        "caller without the lease can write a locked resource");
      Assert.IsTrue(locks.HoldsLock(resource, alice) &&
        !locks.HoldsLock(resource, null),
        "explicit ownership check disagrees with the lease");

      Assert.IsTrue(!locks.TryRenew(resource, "not-a-token"),
        "wrong token renewed the lease");
      Assert.IsTrue(locks.TryRenew(resource, alice),
        "holder could not renew the lease");
      Assert.IsTrue(!locks.TryRelease(resource, "not-a-token") &&
        locks.Get(resource)?.holderName == "Alice",
        "wrong token released the lease");
      Assert.IsTrue(locks.TryRelease(resource, alice),
        "holder could not release the lease");
      Assert.IsTrue(locks.Get(resource) == null && locks.CanWrite(resource, null),
        "released resource remained locked");
    }

    [TestMethod]
    public void ConcurrentAcquire() {
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
          string? token = locks.TryAcquire(
            "domeCalibration", "client-" + contender, out _);
          if (token != null) {
            tokens.Add(token);
          }
        }) {
          IsBackground = true,
        };
        threads[i].Start();
      }

      Assert.IsTrue(ready.Wait(TimeSpan.FromSeconds(5)),
        "lock contenders did not become ready");
      start.Set();
      foreach (Thread thread in threads) {
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)),
          "lock contender did not finish");
      }
      Assert.IsTrue(tokens.Count == 1,
        "concurrent acquisition produced " + tokens.Count + " holders");
      Assert.IsTrue(locks.ActiveLocks().Count == 1,
        "concurrent acquisition published multiple leases");
    }

  }
}

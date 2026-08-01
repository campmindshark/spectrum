using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Spectrum.LayerPipeline.Tests {

  [TestClass]
  public sealed class LayerPipelineTests {
    private static readonly IReadOnlyDictionary<string, Action> TestCases =
      BuildTestCases();

    public static IEnumerable<object[]> DiscoverTestCases() =>
      TestCases.Keys.OrderBy(name => name)
        .Select(name => new object[] { name });

    [TestMethod]
    [DoNotParallelize]
    [DynamicData(nameof(DiscoverTestCases))]
    public void Run(string name) {
      TestCases[name]();
    }

    private static IReadOnlyDictionary<string, Action> BuildTestCases() {
      var tests = new Dictionary<string, Action>();
      void Register(string name, Action test) => tests.Add(name, test);
      WindowsOrchestrationTests.Register(Register);
      WindowsUiControllerTests.Register(Register);
      return tests;
    }
  }
}

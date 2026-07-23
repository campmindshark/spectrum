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
      MotionEmbersTests.Register(Register);
      StackValidatorTests.Register(Register);
      WandProtocolTests.Register(Register);
      PaletteServiceTests.Register(Register);
      AdvisoryLockTests.Register(Register);
      ColorPerformanceTests.Register(Register);
      OPCWireTests.Register(Register);
      ConfigurationContractTests.Register(Register);
      MidiBindingEditorTests.Register(Register);
      PointCloudTests.Register(Register);
      DomeTopologyTests.Register(Register);
      LayerCatalogTests.Register(Register);
      ReactiveVisualizerTests.Register(Register);
      TopologyVisualizerTests.Register(Register);
      ParticleVisualizerTests.Register(Register);
      EnvironmentVisualizerTests.Register(Register);
      LayerRuntimeTests.Register(Register);
      StateOrchestrationTests.Register(Register);
      LayerPipelineCoreTests.Register(Register);
      RenderPlanTests.Register(Register);
      WindowsOrchestrationTests.Register(Register);
      CompositeOperationTests.Register(Register);
      return tests;
    }
  }
}

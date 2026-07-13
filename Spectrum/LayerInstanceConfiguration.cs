using System;
using System.Collections.Generic;
using System.Reflection;
using Spectrum.Base;

namespace Spectrum {

  // Compatibility boundary for the existing visualizer implementations. Each
  // independently-created renderer receives its compiled runtime parameters
  // separately from shared application configuration, while transient
  // counters are still translated from instance ID to the legacy renderer key.
  internal class LayerInstanceConfiguration : DispatchProxy {
    private Configuration target;
    private string instanceId;
    private string rendererId;
    private LayerRendererRuntime runtime;

    public static Configuration Create(
      Configuration target, string instanceId, string rendererId,
      LayerRendererRuntime runtime
    ) {
      Configuration proxy = Create<
        LayerRuntimeConfiguration,
        LayerInstanceConfiguration>();
      var implementation = (LayerInstanceConfiguration)(object)proxy;
      implementation.target = target;
      implementation.instanceId = instanceId;
      implementation.rendererId = rendererId;
      implementation.runtime = runtime;
      return proxy;
    }

    protected override object Invoke(MethodInfo method, object[] args) {
      if (method.Name == "get_LayerRuntime") {
        return this.runtime;
      }
      if (method.Name == "get_domeLayerFireCounters") {
        return this.CounterView(this.target.domeLayerFireCounters);
      }
      if (method.Name == "get_domeLayerClearCounters") {
        return this.CounterView(this.target.domeLayerClearCounters);
      }
      try {
        return method.Invoke(this.target, args);
      } catch (TargetInvocationException error) when (error.InnerException != null) {
        throw error.InnerException;
      }
    }

    private Dictionary<string, int> CounterView(Dictionary<string, int> source) {
      if (source == null) {
        return null;
      }
      var view = new Dictionary<string, int>(source);
      if (source.TryGetValue(this.instanceId, out int value)) {
        view[this.rendererId] = value;
      }
      return view;
    }
  }
}

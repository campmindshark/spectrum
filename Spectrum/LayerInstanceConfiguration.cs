using System;
using System.Collections.Generic;
using System.Reflection;
using Spectrum.Base;

namespace Spectrum {

  // Compatibility boundary for the existing visualizer implementations. Each
  // independently-created renderer sees only its own immutable stack entry and
  // instance-addressed transient counters; all unrelated configuration calls
  // forward to the shared application configuration.
  internal class LayerInstanceConfiguration : DispatchProxy {
    private Configuration target;
    private string instanceId;
    private string rendererId;
    private List<DomeLayerSettings> sourceStack;
    private List<DomeLayerSettings> instanceStack;

    public static Configuration Create(
      Configuration target, string instanceId, string rendererId
    ) {
      Configuration proxy = Create<Configuration, LayerInstanceConfiguration>();
      var implementation = (LayerInstanceConfiguration)(object)proxy;
      implementation.target = target;
      implementation.instanceId = instanceId;
      implementation.rendererId = rendererId;
      return proxy;
    }

    protected override object Invoke(MethodInfo method, object[] args) {
      if (method.Name == "get_domeLayerStack") {
        return this.StackView();
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

    private List<DomeLayerSettings> StackView() {
      List<DomeLayerSettings> current = this.target.domeLayerStack;
      if (ReferenceEquals(current, this.sourceStack)) {
        return this.instanceStack;
      }
      this.sourceStack = current;
      DomeLayerSettings layer = DomeLayerSettings.ForInstance(
        current, this.instanceId);
      this.instanceStack = layer == null
        ? new List<DomeLayerSettings>()
        : new List<DomeLayerSettings> { layer };
      return this.instanceStack;
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

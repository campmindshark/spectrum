using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Spectrum.Base {

  // Live engine counters — the "runtime telemetry" partition split out of
  // Configuration. Never persisted; written by
  // background threads (the operator loop and the OPC send-rate callback) and
  // read by the native FPS labels and the web SSE feed. INotifyPropertyChanged
  // is the contract both consumers already speak: WPF marshals cross-thread
  // scalar-binding notifications to the UI thread itself, and ConfigEventStream
  // fans frames onto thread-safe channels from whatever thread raised the
  // event. Created and exposed by the Operator.
  public class RuntimeTelemetry : INotifyPropertyChanged {

    public event PropertyChangedEventHandler PropertyChanged;

    private void SetField<T>(ref T field, T value,
        [CallerMemberName] string name = null) {
      if (EqualityComparer<T>.Default.Equals(field, value)) {
        return;
      }
      field = value;
      this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Operator loop frames per second, written once a second by OperatorThread.
    private int operatorFPS;
    public int OperatorFPS {
      get => this.operatorFPS;
      set => this.SetField(ref this.operatorFPS, value);
    }

    // OPC frames per second actually sent to the dome BeagleBone, written by
    // the OPCAPI send thread via LEDDomeOutput's callback.
    private int domeBeagleboneOPCFPS;
    public int DomeBeagleboneOPCFPS {
      get => this.domeBeagleboneOPCFPS;
      set => this.SetField(ref this.domeBeagleboneOPCFPS, value);
    }

    // Null while the current layer generation is valid. If construction or
    // compilation fails, the operator retains the previous plan and publishes
    // the rejection reason here for native/web diagnostics.
    private string layerPlanError;
    public string LayerPlanError {
      get => this.layerPlanError;
      set => this.SetField(ref this.layerPlanError, value);
    }
  }
}

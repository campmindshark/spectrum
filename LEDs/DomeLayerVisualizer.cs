using Spectrum.Base;
using System.Collections.Generic;

namespace Spectrum.LEDs {

  /**
   * A "normal" (layerable) dome visualizer. Rather than owning the wire, it
   * renders only into its own persistent LEDDomeOutputBuffer (fades, trails,
   * drawing) and exposes that buffer via LayerBuffer for the compositor in
   * LEDDomeOutput to blend into the composite frame. It never calls
   * WriteBuffer/Flush itself.
   *
   * Implementing this interface is what structurally marks a visualizer as
   * *normal*: debug visualizers (the test patterns and mapping calibration)
   * never implement it, keep the raw SetPixel + self-Flush path, and therefore
   * can never appear in a layer stack.
   *
   * LayerKey is a stable string id used in config files.
   */
  public interface DomeLayerVisualizer : Visualizer, ILayerRenderer {
    string LayerKey { get; }
    LEDDomeOutputBuffer LayerBuffer { get; }
    string ILayerRenderer.RendererId => this.LayerKey;
    LEDDomeOutputBuffer ILayerRenderer.Frame => this.LayerBuffer;
    bool ILayerRenderer.IsAvailable => this.Enabled;
    IReadOnlyList<Input> ILayerRenderer.RequiredInputs => this.GetInputs();
  }

}

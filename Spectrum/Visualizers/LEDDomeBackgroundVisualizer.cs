using Spectrum.Base;
using Spectrum.LEDs;

namespace Spectrum.Visualizers {

  // Fills its whole buffer with a single flat color every frame — a "just a
  // background color" layer, tunable via its one Color param ("color"). Meant
  // to sit at the bottom of the stack under Over so it shows through wherever
  // the layers above it are transparent; center-free and input-free like
  // Twinkle, since it has no data source of its own.
  class LEDDomeBackgroundVisualizer : DomeLayerVisualizer {

    private readonly Configuration config;
    private readonly LEDDomeOutput dome;
    private readonly LEDDomeOutputBuffer buffer;

    public LEDDomeBackgroundVisualizer(
      Configuration config,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeOutputBuffer();
    }

    public int Priority {
      get {
        return DomeLayerSettings.StackActivates(
          this.config.domeLayerStack, "background"
        ) ? 2 : 0;
      }
    }

    public string LayerKey => "background";
    public LEDDomeOutputBuffer LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { });
    }

    public void Visualize() {
      int color = (int)DomeLayerSettings.ParamValue(
        this.config.domeLayerStack, this.LayerKey, "color"
      );
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        this.buffer.pixels[i].color = color;
      }
    }
  }
}

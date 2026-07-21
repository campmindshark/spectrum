using Spectrum.Base;
using Spectrum.LEDs;

namespace Spectrum.Visualizers {

  // Fills its whole buffer with a single flat color every frame — a "just a
  // background color" layer, tunable via its one Color param ("color"). Meant
  // to sit at the bottom of the stack under Over so it shows through wherever
  // the layers above it are transparent; center-free and input-free like
  // Twinkle, since it has no data source of its own.
  class LEDDomeBackgroundVisualizer : DomeLayerVisualizer {

    private readonly LayerRendererRuntime runtime;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;

    public LEDDomeBackgroundVisualizer(
      LayerRendererRuntime runtime,
      DomeRenderContext dome
    ) {
      this.runtime = runtime;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
    }

    public int Priority => 2;

    public string LayerKey => "background";
    public DomeFrame LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { });
    }

    public void Visualize() {
      int color = this.runtime.GetOptions<BackgroundLayerOptions>().Color;
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        this.buffer.pixels[i].color = color;
      }
    }
  }
}

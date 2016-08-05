using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.MIDI;

namespace Spectrum {

  class LEDPanelConfigVisualizer : Visualizer {

    private Configuration config;
    private MidiInput midi;

    public LEDPanelConfigVisualizer(
      Configuration config,
      MidiInput midi,
      CartesianTeensyOutput teensy
    ) {
      this.config = config;
      this.midi = midi;
      // We register here so that we're only run if the LED panel is on
      teensy.RegisterVisualizer(this);
    }

    public int Priority {
      get {
        return -1;
      }
    }

    // We don't actually care about this
    public bool Enabled { get; set; } = false;

    public Input[] GetInputs() {
      return new Input[] { this.midi };
    }

    public void Visualize() {
      var value = this.midi.GetKnobValue(1);
      if (value != -1.0) {
        this.config.ledBoardBrightness = value;
      }
    }

  }

}
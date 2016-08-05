using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.MIDI;

namespace Spectrum {

  class LEDPanelMidiVisualizer : Visualizer {

    private Configuration config;
    private MidiInput midi;
    private CartesianTeensyOutput teensy;

    public LEDPanelMidiVisualizer(
      Configuration config,
      MidiInput midi,
      CartesianTeensyOutput teensy
    ) {
      this.config = config;
      this.midi = midi;
      this.teensy = teensy;
      this.teensy.RegisterVisualizer(this);
    }

    public int Priority {
      get {
        return 1;
      }
    }

    // We don't actually care about this
    public bool Enabled { get; set; } = false;

    public Input[] GetInputs() {
      return new Input[] { this.midi };
    }

    public void Visualize() {
      /*int numColumnsToLight = 5;
      for (int j = 0; j < this.config.teensyRowsPerStrip * 8; j++) {
        for (int i = 0; i < this.config.teensyRowLength; i++) {
          int color = numColumnsToLight > i ? 0x111111 : 0x000000;
          this.teensy.SetPixel(i, j, color);
        }
      }
      this.teensy.Flush();*/
    }

  }

}
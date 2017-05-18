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
    private LEDBoardOutput teensy;
    private List<KeyValuePair<int, double>> currentlyOn;

    public LEDPanelMidiVisualizer(
      Configuration config,
      MidiInput midi,
      LEDBoardOutput teensy
    ) {
      this.config = config;
      this.midi = midi;
      this.teensy = teensy;
      this.teensy.RegisterVisualizer(this);
      this.currentlyOn = new List<KeyValuePair<int, double>>();
    }

    public int Priority {
      get {
        return this.currentlyOn.Count == 0 ? 1 : 2;
      }
    }

    // We don't actually care about this
    public bool Enabled { get; set; } = false;

    public Input[] GetInputs() {
      return new Input[] { this.midi };
    }

    public void Visualize() {
      foreach (MidiCommand command in this.midi.GetCommandsSinceLastTick()) {
        if (command.type != MidiCommandType.Note) {
          continue;
        }
        if (command.index < 48 || command.index > 51) {
          continue;
        }
        if (command.value == 0.0) {
          this.currentlyOn.RemoveAll(pair => pair.Key == command.index);
        } else {
          this.currentlyOn.Add(
            new KeyValuePair<int, double>(command.index, command.value)
          );
        }
      }

      if (this.currentlyOn.Count == 0) {
        return;
      }
      var mostRecentCommand = this.currentlyOn.Last();

      int color = 0;
      int brightnessByte =
        (int)(0xFF * this.config.boardBrightness * mostRecentCommand.Value);
      if (mostRecentCommand.Key == 48) {
        color = brightnessByte << 16;
      } else if (mostRecentCommand.Key == 49) {
        color = brightnessByte << 8;
      } else if (mostRecentCommand.Key == 50) {
        color = brightnessByte;
      } else if (mostRecentCommand.Key == 51) {
        color = brightnessByte | brightnessByte << 8 | brightnessByte << 16;
      }

      for (int j = 0; j < this.config.boardRowsPerStrip * 8; j++) {
        for (int i = 0; i < this.config.boardRowLength; i++) {
          this.teensy.SetPixel(i, j, color);
        }
      }
      this.teensy.Flush();
    }

  }

}
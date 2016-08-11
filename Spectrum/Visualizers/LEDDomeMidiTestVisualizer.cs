using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.MIDI;

namespace Spectrum {

  class LEDDomeMidiTestVisualizer : Visualizer {

    private Configuration config;
    private MidiInput midi;
    private LEDDomeOutput dome;
    // Map from note to strut it has turned on
    private Dictionary<int, int> strutStates;
    private Random rand;

    public LEDDomeMidiTestVisualizer(
      Configuration config,
      MidiInput midi,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.midi = midi;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.strutStates = new Dictionary<int, int>();
      this.rand = new Random();
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
      var commands = this.midi.GetCommandsSinceLastTick();
      bool valueSet = false;
      foreach (MidiCommand command in commands) {
        if (command.type != MidiCommandType.Note) {
          continue;
        }
        if (command.index < 48 || command.index > 51) {
          continue;
        }

        valueSet = true;
        if (this.strutStates.ContainsKey(command.index)) {
          for (int i = 0; i < 30; i++) {
            this.dome.SetPixel(this.strutStates[command.index], i, 0x000000);
          }
          this.strutStates.Remove(command.index);
          if (command.value == 0.0) {
            continue;
          }
        }

        int color = 0;
        int brightnessByte =
          (int)(0xFF * this.config.domeMaxBrightness * command.value);
        if (command.index == 48) {
          color = brightnessByte << 16;
        } else if (command.index == 49) {
          color = brightnessByte << 8;
        } else if (command.index == 50) {
          color = brightnessByte;
        } else if (command.index == 51) {
          color = brightnessByte | brightnessByte << 8 | brightnessByte << 16;
        }

        int strutIndex = -1;
        while (strutIndex == -1) {
          int candidateStrutIndex = (int)(this.rand.NextDouble() * 190);
          if (this.strutStates.ContainsValue(candidateStrutIndex)) {
            continue;
          }
          strutIndex = candidateStrutIndex;
        }

        for (int i = 0; i < 30; i++) {
          this.dome.SetPixel(strutIndex, i, color);
        }
        this.strutStates[command.index] = strutIndex;
      }

      if (valueSet) {
        this.dome.Flush();
      }
    }

  }

}
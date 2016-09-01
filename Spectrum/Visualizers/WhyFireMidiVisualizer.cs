using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.MIDI;
using Spectrum.WhyFire;

namespace Spectrum {

  class WhyFireMidiVisualizer : Visualizer {

    private Configuration config;
    private MidiInput midi;
    private WhyFireOutput whyFire;

    public WhyFireMidiVisualizer(
      Configuration config,
      MidiInput midi,
      WhyFireOutput whyFire
    ) {
      this.config = config;
      this.midi = midi;
      this.whyFire = whyFire;
      this.whyFire.RegisterVisualizer(this);
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
      foreach (MidiCommand command in commands) {
        if (command.type != MidiCommandType.Note) {
          continue;
        }
        if (command.index < 48 || command.index > 63) {
          continue;
        }
        if (command.value == 0.0) {
          continue;
        }
        if (command.index < 55) {
          this.whyFire.FireEffect(command.index - 47);
        } else if (command.index == 55) {
          this.whyFire.FireAll();
        } else if (command.index == 56) {
          this.whyFire.Winston();
        } else if (command.index == 57) {
          this.whyFire.WhyNot();
        } else if (command.index == 58) {
          this.whyFire.StayOut();
        } else if (command.index == 59) {
          this.whyFire.Alternate();
        } else if (command.index == 60) {
          this.whyFire.SweepLeft();
        } else if (command.index == 61) {
          this.whyFire.SweepRight();
        }
      }
    }

  }

}

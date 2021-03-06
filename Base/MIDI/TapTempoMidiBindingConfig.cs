﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.Base {

  using BindingKey = Tuple<MidiCommandType, int>;

  public class TapTempoMidiBindingConfig : MidiBindingConfig {

    public MidiCommandType buttonType { get; set; }
    public int buttonIndex { get; set; }

    public TapTempoMidiBindingConfig() {
      this.BindingType = 1;
    }

    public override object Clone() {
      return new TapTempoMidiBindingConfig() {
        BindingName = this.BindingName,
        buttonType = this.buttonType,
        buttonIndex = this.buttonIndex,
      };
    }

    public override Binding[] GetBindings(Configuration config) {
      Binding binding = new Binding();
      binding.key = new BindingKey(this.buttonType, this.buttonIndex);
      binding.config = this;
      binding.callback = (index, val) => {
        if (val == 0.0) {
          return null;
        }
        config.beatBroadcaster.AddTap();
        return "tap registered!";
      };
      return new Binding[] { binding };
    }

  }

}
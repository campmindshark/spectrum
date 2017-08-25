using System;

namespace Spectrum.Base {

  using BindingKey = Tuple<MidiCommandType, int>;

  public class ColorPaletteMidiBindingConfig : MidiBindingConfig {

    public int indexRangeStart { get; set; }

    public ColorPaletteMidiBindingConfig() {
      this.BindingType = 0;
    }

    public override object Clone() {
      return new ColorPaletteMidiBindingConfig() {
        BindingName = this.BindingName,
        indexRangeStart = this.indexRangeStart,
      };
    }

    public override Binding[] GetBindings(Configuration config) {
      Binding binding = new Binding();
      binding.key = new BindingKey(MidiCommandType.Note, -1);
      binding.config = this;
      binding.callback = (index, val) => {
        if (index < this.indexRangeStart || index > (this.indexRangeStart + 7)) {
          return null;
        }
        var colorPaletteIndex = index - this.indexRangeStart;
        config.colorPaletteIndex = colorPaletteIndex;
        return "updating color palette to " + colorPaletteIndex;
      };
      return new Binding[] { binding };
    }

  }

}
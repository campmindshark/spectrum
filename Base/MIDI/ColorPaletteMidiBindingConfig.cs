using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.Base {

  using BindingKey = Tuple<MidiCommandType, int>;

  public class ColorPaletteMidiBindingConfig : MidiBindingConfig {

    // Each key on the keyboard corresponds to a color
    private static int[] colorFromColorIndex = new int[] {
      0x000000, 0xFF0000, 0xFF3232, 0xFE00FF, 0xFD32FF, 0xFD54FF, 0xA100FF,
      0xA432FF, 0xA954FF, 0x0055FF, 0x3262FF, 0x00D5FF, 0x33D9FF, 0x54DEFF,
      0x00FFB9, 0x33FFBA, 0x39FF00, 0x50FF34, 0xE6FF00, 0xE8FF34, 0xFFD300,
      0xFFD334, 0xFF7100, 0xFF7834, 0xFFFFFF,
      /*0x000000, 0xFF0000, 0xFF4400, 0xFF8800, 0xFFCC00, 0xFFFF00, 0xCCFF00,
      0x88FF00, 0x44FF00, 0x00FF00, 0x00FF44, 0x00FF88, 0x00FFCC, 0x00FFFF,
      0x00CCFF, 0x0088FF, 0x0044FF, 0x0000FF, 0x4400FF, 0x8800FF, 0xCC00FF,
      0xFF00FF, 0xFF55FF, 0xFFABFF, 0xFFFFFF,*/
    };

    public MidiCommandType indexRangeType { get; set; }
    public int indexRangeStart { get; set; }
    public int indexRangeEnd { get; set; }
    public MidiCommandType colorRangeType { get; set; }
    public int colorRangeStart { get; set; }
    public int colorRangeEnd { get; set; }

    private int currentIndex = -1;
    private int currentFirstColor = -1;

    public ColorPaletteMidiBindingConfig() {
      this.BindingType = 0;
    }

    public override object Clone() {
      return new ColorPaletteMidiBindingConfig() {
        BindingName = this.BindingName,
        indexRangeType = this.indexRangeType,
        indexRangeStart = this.indexRangeStart,
        indexRangeEnd = this.indexRangeEnd,
        colorRangeStart = this.colorRangeStart,
        colorRangeEnd = this.colorRangeEnd,
      };
    }

    private void SetColor(Configuration config, int colorIndex) {
      if (this.currentFirstColor != -1) {
        config.colorPalette.SetGradientColor(
          this.currentIndex,
          colorFromColorIndex[this.currentFirstColor],
          colorFromColorIndex[colorIndex]
        );
      } else {
        this.currentFirstColor = colorIndex;
        config.colorPalette.SetColor(
          this.currentIndex,
          colorFromColorIndex[colorIndex]
        );
      }
    }

    public override Binding[] GetBindings(Configuration config) {
      Binding programBinding = new Binding();
      programBinding.key = new BindingKey(MidiCommandType.Program, -1);
      programBinding.callback = (index, val) => {
        if (
          this.colorRangeType == MidiCommandType.Program &&
          index >= this.colorRangeStart && index <= this.colorRangeEnd &&
          this.currentIndex != -1
        ) {
          var colorIndex = index - this.colorRangeStart;
          if (val > 0) {
            this.SetColor(config, colorIndex);
          } else if (colorIndex == this.currentFirstColor) {
            this.currentFirstColor = -1;
          }
          return;
        } else {
          this.currentFirstColor = -1;
        }
        if (
          indexRangeType == MidiCommandType.Program &&
          index >= this.indexRangeStart && index <= this.indexRangeEnd
        ) {
          this.currentIndex = index - this.indexRangeStart;
        } else {
          this.currentIndex = -1;
        }
      };

      Binding knobBinding = new Binding();
      knobBinding.key = new BindingKey(MidiCommandType.Knob, -1);
      knobBinding.callback = (index, val) => {
        if (
          this.colorRangeType == MidiCommandType.Knob &&
          index >= this.colorRangeStart && index <= this.colorRangeEnd &&
          this.currentIndex != -1
        ) {
          var colorIndex = index - this.colorRangeStart;
          if (val > 0) {
            this.SetColor(config, colorIndex);
          } else if (colorIndex == this.currentFirstColor) {
            this.currentFirstColor = -1;
          }
          return;
        } else {
          this.currentFirstColor = -1;
        }
        if (
          indexRangeType == MidiCommandType.Knob &&
          index >= this.indexRangeStart && index <= this.indexRangeEnd
        ) {
          this.currentIndex = index - this.indexRangeStart;
        } else {
          this.currentIndex = -1;
        }
      };

      Binding noteBinding = new Binding();
      noteBinding.key = new BindingKey(MidiCommandType.Note, -1);
      noteBinding.callback = (index, val) => {
        if (
          this.colorRangeType == MidiCommandType.Note &&
          index >= this.colorRangeStart && index <= this.colorRangeEnd &&
          this.currentIndex != -1
        ) {
          var colorIndex = index - this.colorRangeStart;
          if (val > 0) {
            this.SetColor(config, colorIndex);
          } else if (colorIndex == this.currentFirstColor) {
            this.currentFirstColor = -1;
          }
          return;
        } else {
          this.currentFirstColor = -1;
        }
        if (
          indexRangeType == MidiCommandType.Note &&
          index >= this.indexRangeStart && index <= this.indexRangeEnd
        ) {
          this.currentIndex = index - this.indexRangeStart;
        } else {
          this.currentIndex = -1;
        }
      };

      return new Binding[] { programBinding, knobBinding, noteBinding };
    }

  }

}
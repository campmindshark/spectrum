using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Xml.Serialization;

namespace Spectrum.Base {

  public class LEDColorPalette : INotifyPropertyChanged {

    // Public so XML serialization picks it up
    // Do not set directly!!
    public LEDColor[] colors = new LEDColor[16];

    public event PropertyChangedEventHandler PropertyChanged;

    public int GetSingleColor(int index) {
      return this.colors[index].Color1;
    }

    public int GetGradientColor(int index, double pixelPos, double focusPos) {
      return this.colors[index].GradientColor(pixelPos, focusPos);
    }

    public void SetColor(int index, int color) {
      this.colors[index] = new LEDColor(color);
      this.CallPropertyChanged();
    }

    public void SetGradientColor(int index, int color1, int color2) {
      this.colors[index] = new LEDColor(color1, color2);
      this.CallPropertyChanged();
    }

    private void CallPropertyChanged() {
      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs(Binding.IndexerName)
      );
    }

    [XmlIgnore]
    public int? this[int index, int whichColor] {
      get {
        return this.GetColor(index, whichColor);
      }
      set {
        if (whichColor == 0) {
          this.SetColor0(index, value);
        } else if (whichColor == 1) {
          this.SetColor1(index, value);
        } else {
          throw new Exception("unsupported whichColor");
        }
        this.CallPropertyChanged();
      }
    }

    private int? GetColor(int index, int whichColor) {
      if (this.colors[index] == null) {
        return null;
      }
      if (whichColor == 0) {
        return this.colors[index].Color1;
      }
      if (!this.colors[index].IsGradient) {
        return null;
      }
      return this.colors[index].Color2;
    }

    private void SetColor0(int index, int? value) {
      if (!value.HasValue) {
        this.colors[index] = null;
      } else if (this.colors[index] != null && this.colors[index].IsGradient) {
        this.colors[index] = new LEDColor(value.Value, this.colors[index].Color2);
      } else {
        this.colors[index] = new LEDColor(value.Value);
      }
    }

    private void SetColor1(int index, int? value) {
      int color1 = this.colors[index] == null
        ? 0x000000
        : this.colors[index].Color1;
      if (value.HasValue) {
        this.colors[index] = new LEDColor(color1, value.Value);
      } else {
        this.colors[index] = new LEDColor(color1);
      }
    }

  }

  public class LEDColor {

    // Public so XML serialization picks them up
    public int color1;
    public int? color2;

    // We need a parameterless constructor for XML serialization
    public LEDColor() { }

    public LEDColor(int color) {
      this.color1 = color;
      this.color2 = null;
    }

    public LEDColor(int color1, int color2) {
      this.color1 = color1;
      this.color2 = color2;
    }

    [XmlIgnore]
    public bool IsGradient {
      get {
        return this.color2.HasValue;
      }
    }

    [XmlIgnore]
    public int Color1 {
      get {
        return this.color1;
      }
    }

    [XmlIgnore]
    public int Color2 {
      get {
        return this.color2.Value;
      }
    }

    public int GradientColor(double pixelPos, double focusPos) {
      // Distance given that 1.0 wraps to 0.0
      double distance = Math.Min(
        Math.Abs(pixelPos - focusPos),
        1 - Math.Abs(pixelPos - focusPos)
      );
      byte redA = (byte)(this.color1 >> 16);
      byte greenA = (byte)(this.color1 >> 8);
      byte blueA = (byte)this.color1;
      byte redB = (byte)(this.color2.Value >> 16);
      byte greenB = (byte)(this.color2.Value >> 8);
      byte blueB = (byte)this.color2.Value;
      byte blendedRed = (byte)((distance * redA) + (1 - distance) * redB);
      byte blendedGreen = (byte)((distance * greenA) + (1 - distance) * greenB);
      byte blendedBlue = (byte)((distance * blueA) + (1 - distance) * blueB);
      return (blendedRed << 16) | (blendedGreen << 8) | blendedBlue;
    }

  }

}

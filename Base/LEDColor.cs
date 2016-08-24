using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
      this.CallPropertyChanged(index, 0);
    }

    public void SetGradientColor(int index, int color1, int color2) {
      this.colors[index] = new LEDColor(color1, color2);
      this.CallPropertyChanged(index, 0);
      this.CallPropertyChanged(index, 1);
    }

    /**
     * All the stuff below is to make this work with IPropertyChanged and UI
     * binding...
     */

    // index: 0-15
    // whichColor: 0-1, Color1 or Color2
    private void CallPropertyChanged(int index, int whichColor) {
      StringBuilder builder = new StringBuilder("Color");
      builder.Append(whichColor + 1);
      builder.Append('_');
      builder.Append(index);
      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs(builder.ToString())
      );
    }

    private void SetColor1(int index, int? value) {
      if (!value.HasValue) {
        this.colors[index] = null;
      } else if (this.colors[index] != null && this.colors[index].IsGradient) {
        this.colors[index] = new LEDColor(value.Value, this.colors[index].Color2);
      } else {
        this.colors[index] = new LEDColor(value.Value);
      }
      this.CallPropertyChanged(index, 0);
    }

    private void SetColor2(int index, int? value) {
      int color1 = this.colors[index] == null
        ? 0x000000
        : this.colors[index].Color1;
      if (value.HasValue) {
        this.colors[index] = new LEDColor(color1, value.Value);
      } else {
        this.colors[index] = new LEDColor(color1);
      }
      this.CallPropertyChanged(index, 1);
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

    [XmlIgnore]
    public int? Color1_0 {
      get { return this.GetColor(0, 0); }
      set { this.SetColor1(0, value); }
    }
    [XmlIgnore]
    public int? Color1_1 {
      get { return this.GetColor(1, 0); }
      set { this.SetColor1(1, value); }
    }
    [XmlIgnore]
    public int? Color1_2 {
      get { return this.GetColor(2, 0); }
      set { this.SetColor1(2, value); }
    }
    [XmlIgnore]
    public int? Color1_3 {
      get { return this.GetColor(3, 0); }
      set { this.SetColor1(3, value); }
    }
    [XmlIgnore]
    public int? Color1_4 {
      get { return this.GetColor(4, 0); }
      set { this.SetColor1(4, value); }
    }
    [XmlIgnore]
    public int? Color1_5 {
      get { return this.GetColor(5, 0); }
      set { this.SetColor1(5, value); }
    }
    [XmlIgnore]
    public int? Color1_6 {
      get { return this.GetColor(6, 0); }
      set { this.SetColor1(6, value); }
    }
    [XmlIgnore]
    public int? Color1_7 {
      get { return this.GetColor(7, 0); }
      set { this.SetColor1(7, value); }
    }
    [XmlIgnore]
    public int? Color1_8 {
      get { return this.GetColor(8, 0); }
      set { this.SetColor1(8, value); }
    }
    [XmlIgnore]
    public int? Color1_9 {
      get { return this.GetColor(9, 0); }
      set { this.SetColor1(9, value); }
    }
    [XmlIgnore]
    public int? Color1_10 {
      get { return this.GetColor(10, 0); }
      set { this.SetColor1(10, value); }
    }
    [XmlIgnore]
    public int? Color1_11 {
      get { return this.GetColor(11, 0); }
      set { this.SetColor1(11, value); }
    }
    [XmlIgnore]
    public int? Color1_12 {
      get { return this.GetColor(12, 0); }
      set { this.SetColor1(12, value); }
    }
    [XmlIgnore]
    public int? Color1_13 {
      get { return this.GetColor(13, 0); }
      set { this.SetColor1(13, value); }
    }
    [XmlIgnore]
    public int? Color1_14 {
      get { return this.GetColor(14, 0); }
      set { this.SetColor1(14, value); }
    }
    [XmlIgnore]
    public int? Color1_15 {
      get { return this.GetColor(15, 0); }
      set { this.SetColor1(15, value); }
    }
    [XmlIgnore]
    public int? Color2_0 {
      get { return this.GetColor(0, 1); }
      set { this.SetColor2(0, value); }
    }
    [XmlIgnore]
    public int? Color2_1 {
      get { return this.GetColor(1, 1); }
      set { this.SetColor2(1, value); }
    }
    [XmlIgnore]
    public int? Color2_2 {
      get { return this.GetColor(2, 1); }
      set { this.SetColor2(2, value); }
    }
    [XmlIgnore]
    public int? Color2_3 {
      get { return this.GetColor(3, 1); }
      set { this.SetColor2(3, value); }
    }
    [XmlIgnore]
    public int? Color2_4 {
      get { return this.GetColor(4, 1); }
      set { this.SetColor2(4, value); }
    }
    [XmlIgnore]
    public int? Color2_5 {
      get { return this.GetColor(5, 1); }
      set { this.SetColor2(5, value); }
    }
    [XmlIgnore]
    public int? Color2_6 {
      get { return this.GetColor(6, 1); }
      set { this.SetColor2(6, value); }
    }
    [XmlIgnore]
    public int? Color2_7 {
      get { return this.GetColor(7, 1); }
      set { this.SetColor2(7, value); }
    }
    [XmlIgnore]
    public int? Color2_8 {
      get { return this.GetColor(8, 1); }
      set { this.SetColor2(8, value); }
    }
    [XmlIgnore]
    public int? Color2_9 {
      get { return this.GetColor(9, 1); }
      set { this.SetColor2(9, value); }
    }
    [XmlIgnore]
    public int? Color2_10 {
      get { return this.GetColor(10, 1); }
      set { this.SetColor2(10, value); }
    }
    [XmlIgnore]
    public int? Color2_11 {
      get { return this.GetColor(11, 1); }
      set { this.SetColor2(11, value); }
    }
    [XmlIgnore]
    public int? Color2_12 {
      get { return this.GetColor(12, 1); }
      set { this.SetColor2(12, value); }
    }
    [XmlIgnore]
    public int? Color2_13 {
      get { return this.GetColor(13, 1); }
      set { this.SetColor2(13, value); }
    }
    [XmlIgnore]
    public int? Color2_14 {
      get { return this.GetColor(14, 1); }
      set { this.SetColor2(14, value); }
    }
    [XmlIgnore]
    public int? Color2_15 {
      get { return this.GetColor(15, 1); }
      set { this.SetColor2(15, value); }
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

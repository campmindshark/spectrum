using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public LEDColor[] colors { get; set; }
    // Don't change the collection, but feel free to set values in it
    public ComputerEnabledColors computerEnabledColors { get; set; } =
      new ComputerEnabledColors();

    public event PropertyChangedEventHandler PropertyChanged;

    public int GetSingleColor(int index) {
      if (this.colors == null || this.colors[index] == null) {
        return 0x000000;
      }
      return this.colors[index].Color1;
    }

    public int GetGradientColor(
      int index,
      double pixelPos,
      double focusPos,
      bool wrap
    ) {
      if (this.colors == null || this.colors[index] == null) {
        return 0x000000;
      }
      return this.colors[index].GradientColor(pixelPos, focusPos, wrap);
    }

    /**
     * This method's different from GetSingleColor is that it uses an "enabled
     * index", ie. "the nth color that is computer-enabled". Visualizers that
     * are algorithmically driven should use this method, so that the user is
     * able to decide which colors they are allowed to use.
     */
    public int GetSingleComputerColor(int enabledIndex) {
      int? index = this.GetIndexOfEnabledIndex(enabledIndex);
      if (!index.HasValue || this.colors[index.Value] == null) {
        return 0x000000;
      }
      return this.colors[index.Value].Color1;
    }

    /**
     * This method's difference from GetGradientColor is that it uses an
     * "enabled index", ie. "the nth color that is computer-enabled".
     * Visualizers that are algorithmically driven should use this method,so
     * that the user is able to decide which colors they are allowed to use.
     */
    public int GetGradientComputerColor(
      int enabledIndex,
      double pixelPos,
      double focusPos,
      bool wrap
    ) {
      int? index = this.GetIndexOfEnabledIndex(enabledIndex);
      if (!index.HasValue || this.colors[index.Value] == null) {
        return 0x000000;
      }
      return this.colors[index.Value].GradientColor(pixelPos, focusPos, wrap);
    }

    public int? GetIndexOfEnabledIndex(int enabledIndex) {
      if (this.colors == null) {
        return null;
      }
      for (int i = 0; i < 16; i++) {
        if (!this.computerEnabledColors[i] || this.colors[i] == null) {
          continue;
        }
        if (enabledIndex == 0) {
          return i;
        }
        enabledIndex--;
      }
      return null;
    }

    public void SetColor(int index, int color) {
      if (this.colors == null) {
        this.colors = new LEDColor[16];
      }
      this.colors[index] = new LEDColor(color);
      this.CallPropertyChanged();
    }

    public void SetGradientColor(int index, int color1, int color2) {
      if (this.colors == null) {
        this.colors = new LEDColor[16];
      }
      this.colors[index] = new LEDColor(color1, color2);
      this.CallPropertyChanged();
    }

    private void CallPropertyChanged() {
      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs(Binding.IndexerName)
      );
    }

    // This is how the UI binds to us
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
      if (this.colors == null || this.colors[index] == null) {
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
      if (this.colors == null) {
        this.colors = new LEDColor[16];
      }
      if (!value.HasValue) {
        this.colors[index] = null;
      } else if (this.colors[index] != null && this.colors[index].IsGradient) {
        this.colors[index] = new LEDColor(value.Value, this.colors[index].Color2);
      } else {
        this.colors[index] = new LEDColor(value.Value);
      }
    }

    private void SetColor1(int index, int? value) {
      if (this.colors == null) {
        this.colors = new LEDColor[16];
      }
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
    public int color1 { get; set; } = 0;
    public int color2 { get; set; } = 0;
    public bool color2Enabled { get; set; } = false;

    // We need a parameterless constructor for XML serialization
    public LEDColor() { }

    public LEDColor(int color) {
      this.color1 = color;
    }

    public LEDColor(int color1, int color2) {
      this.color1 = color1;
      this.color2 = color2;
      this.color2Enabled = true;
    }

    [XmlIgnore]
    public bool IsGradient {
      get {
        return this.color2Enabled;
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
        if (!this.color2Enabled) {
          throw new Exception();
        }
        return this.color2;
      }
    }

    public int GradientColor(double pixelPos, double focusPos, bool wrap) {
      double distance;
      if (wrap) {
        distance = Math.Min(
          Math.Abs(pixelPos - focusPos),
          1 - Math.Abs(pixelPos - focusPos)
        );
      } else {
        distance = Math.Abs(pixelPos - focusPos);
      }
      byte redA = (byte)(this.Color1 >> 16);
      byte greenA = (byte)(this.Color1 >> 8);
      byte blueA = (byte)this.Color1;
      byte redB = (byte)(this.Color2 >> 16);
      byte greenB = (byte)(this.Color2 >> 8);
      byte blueB = (byte)this.Color2;
      byte blendedRed = (byte)((distance * redA) + (1 - distance) * redB);
      byte blendedGreen = (byte)((distance * greenA) + (1 - distance) * greenB);
      byte blendedBlue = (byte)((distance * blueA) + (1 - distance) * blueB);
      return (blendedRed << 16) | (blendedGreen << 8) | blendedBlue;
    }

    public static int ScaleColor(int color, double scale) {
      byte red = (byte)(color >> 16);
      byte green = (byte)(color >> 8);
      byte blue = (byte)color;
      return (int)(red * scale) << 16
        | (int)(green * scale) << 8
        | (int)(blue * scale);
    }

  }

}

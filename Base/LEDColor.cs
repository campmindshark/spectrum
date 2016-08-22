using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.Base {

  [Serializable]
  public class LEDColorPalette {

    private Configuration config;
    private LEDColor[] colors = new LEDColor[8];

    public LEDColorPalette(Configuration config) {
      this.config = config;
    }

    public int GetSingleColor(int index) {
      LEDColor color = this.colors[index];
      return ScaleColor(color.Color1);
    }

    public int GetGradientColor(int index, double pixelPos, double focusPos) {
      LEDColor color = this.colors[index];
      return ScaleColor(color.GradientColor(pixelPos, focusPos));
    }

    public void SetColor(int index, int color) {
      this.colors[index] = new LEDColor(color);
    }

    public void SetGradientColor(int index, int color1, int color2) {
      this.colors[index] = new LEDColor(color1, color2);
    }

    private int ScaleColor(int color) {
      byte red = (byte)(color >> 16);
      byte green = (byte)(color >> 8);
      byte blue = (byte)color;
      return (int)(red * this.config.domeMaxBrightness) << 16
        | (int)(green * this.config.domeMaxBrightness) << 8
        | (int)(blue * this.config.domeMaxBrightness);
    }

  }

  [Serializable]
  public class LEDColor {

    private int color1;
    private int? color2;

    public LEDColor(int color) {
      this.color1 = color;
      this.color2 = null;
    }

    public LEDColor(int color1, int color2) {
      this.color1 = color1;
      this.color2 = color2;
    }

    public bool IsGradient {
      get {
        return this.color2.HasValue
      }
    }

    public int Color1 {
      get {
        return this.color1;
      }
    }

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

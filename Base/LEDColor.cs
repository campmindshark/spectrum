using System;
using System.Xml.Serialization;

namespace Spectrum.Base {

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

    // Deep-copy constructor. Palette duplication must not alias live LEDColor
    // instances, or an edit to one palette would mutate another.
    public LEDColor(LEDColor other) {
      this.color1 = other.color1;
      this.color2 = other.color2;
      this.color2Enabled = other.color2Enabled;
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
        ) * 2.0;
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

    public static int FromDoubles(double r, double g, double b) {
      int x = (int)(r * 255);
      int y = (int)(g * 255);
      int z = (int)(b * 255);
      int color = (x << 16) | (y << 8) | z;
      return color;
    }

    // Approximate visible-spectrum dispersion ramp: maps t in [0,1] to the
    // colour a prism throws for that fraction of the sweep — R->O->Y->G->C->B->V
    // with the uneven band widths real dispersion has (via Bruton's
    // wavelength->RGB piecewise). Unlike an HSV hue wheel it never passes
    // through magenta, which is what makes it read as "optics" rather than
    // "rainbow". Output channels are 0..255 doubles so it drops straight into
    // the compositor's blend math. t=0 is deep red (645nm),
    // t=1 is violet (380nm).
    public static void SpectralColor(
      double t, out double r, out double g, out double b
    ) {
      if (t < 0) {
        t = 0;
      } else if (t > 1) {
        t = 1;
      }
      double w = 645 - t * (645 - 380);
      double rr, gg, bb;
      if (w < 440) {
        rr = -(w - 440) / (440 - 380); gg = 0; bb = 1;
      } else if (w < 490) {
        rr = 0; gg = (w - 440) / (490 - 440); bb = 1;
      } else if (w < 510) {
        rr = 0; gg = 1; bb = -(w - 510) / (510 - 490);
      } else if (w < 580) {
        rr = (w - 510) / (580 - 510); gg = 1; bb = 0;
      } else if (w < 645) {
        rr = 1; gg = -(w - 645) / (645 - 580); bb = 0;
      } else {
        rr = 1; gg = 0; bb = 0;
      }
      // Gentle intensity falloff at the ends, floored well above 0 so the dome's
      // deep reds/violets stay visibly lit (LED look, not a physics sim).
      double falloff;
      if (w < 420) {
        falloff = 0.35 + 0.65 * (w - 380) / (420 - 380);
      } else if (w > 600) {
        falloff = 0.35 + 0.65 * (645 - w) / (645 - 600);
      } else {
        falloff = 1.0;
      }
      r = rr * falloff * 255;
      g = gg * falloff * 255;
      b = bb * falloff * 255;
    }

    // Packed-int convenience over SpectralColor, so gradient-driven visualizers
    // can borrow the dispersion look as a palette.
    public static int SpectralColor(double t) {
      SpectralColor(t, out double r, out double g, out double b);
      return FromDoubles(r / 255, g / 255, b / 255);
    }

  }

}

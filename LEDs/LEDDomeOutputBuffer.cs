using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.LEDs {

  public struct LEDDomeOutputPixel {
    // index by strut and led within that strut
    public int strutIndex;
    public int strutLEDIndex;
    // index by control box and pixel within that control box
    public int controlBoxIndex;
    public int controlBoxPixelIndex;
    // position in projection
    public double x;
    public double y;

    private int _color;
    private double _r;
    private double _g;
    private double _b;

    // color
    public int color {
      get { return _color; }
      set {
        _color = value;
        var r = (byte)(_color >> 16);
        var g = (byte)(_color >> 8);
        var b = (byte)_color;
        _r = r;
        _g = g;
        _b = b;
      }
    }

    private void updateColor() {
      _color = (int)(((byte)_r) << 16) +
        (int)(((byte)_g) << 8) +
        (int)(((byte)_b));
    }

    public double r {
      get { return _r; }
      set { _r = value; updateColor(); }
    }
    public double g {
      get { return _g; }
      set { _g = value; updateColor(); }
    }
    public double b {
      get { return _b; }
      set { _b = value; updateColor(); }
    }
  }

  public class LEDDomeOutputBuffer {
    public LEDDomeOutputPixel[] pixels;

    public LEDDomeOutputBuffer(LEDDomeOutputPixel[] pixels) {
      this.pixels = pixels;
    }

    public void Fade(double mul, double sub) {
      for (int i = 0; i < pixels.Length; i++) {
        //pixels[i].color /= 2;
        pixels[i].r = pixels[i].r * mul - sub;
        pixels[i].g = pixels[i].g * mul - sub;
        pixels[i].b = pixels[i].b * mul - sub;
      }
    }
  }
}

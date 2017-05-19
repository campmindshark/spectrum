using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum {

  static class SimulatorUtils {

    public static int GetComputerColor(int ledColor) {
      byte red = (byte)(ledColor >> 16);
      byte green = (byte)(ledColor >> 8);
      byte blue = (byte)ledColor;
      double ratio;
      if (red >= green && red >= blue) {
        ratio = (double)0xFF / red;
      } else if (green >= red && green >= blue) {
        ratio = (double)0xFF / green;
      } else {
        ratio = (double)0xFF / blue;
      }
      double factor = Math.Sqrt(ratio);
      return (int)(red * factor) << 16 |
        (int)(green * factor) << 8 |
        (int)(blue * factor);
    }

  }


}
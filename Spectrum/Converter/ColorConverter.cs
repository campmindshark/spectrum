using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace Spectrum {

  class ColorConverter : IValueConverter {

    public object Convert(
      object value,
      Type targetType,
      object parameter,
      System.Globalization.CultureInfo culture
    ) {
      try {
        if (value == null) {
          return null;
        }
        int rgb = (int)value;
        Color color = new Color();
        color.R = (byte)(rgb >> 16);
        color.G = (byte)(rgb >> 8);
        color.B = (byte)rgb;
        color.A = 255;
        return color;
      } catch (FormatException) {
        // Failing to convert will trip a validation rule
        return value;
      }
    }

    public object ConvertBack(
      object value,
      Type targetType,
      object parameter,
      System.Globalization.CultureInfo culture
    ) {
      try {
        if (value == null) {
          return null;
        }
        Color color = (Color)value;
        return (int)color.R << 16
          | (int)color.G << 8
          | (int)color.B;
      } catch (FormatException) {
        // Failing to convert will trip a validation rule
        return value;
      }
    }

  }

}

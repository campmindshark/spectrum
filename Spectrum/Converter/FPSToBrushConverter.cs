using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace Spectrum {

  class FPSToBrushConverter : IValueConverter {

    public object Convert(
      object value,
      Type targetType,
      object parameter,
      System.Globalization.CultureInfo culture
    ) {
      if (!(value is int)) {
        return value;
      }
      int intValue = (int)value;
      if (intValue >= 60) {
        return Brushes.Green;
      } else if (intValue >= 30) {
        return Brushes.Goldenrod;
      } else {
        return Brushes.Red;
      }
    }

    public object ConvertBack(
      object value,
      Type targetType,
      object parameter,
      System.Globalization.CultureInfo culture
    ) {
      // We can't convert back! Only use with OneWay bindings
      return value;
    }

  }

}

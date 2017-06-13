using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Spectrum {

  class StringJoinConverter : IValueConverter {

    public object Convert(
      object value,
      Type targetType,
      object parameter,
      System.Globalization.CultureInfo culture
    ) {
      if (value == null) {
        return "";
      }
      try {
        return String.Join(",", (int[])value);
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
        return Array.ConvertAll(((string)value).Split(','), int.Parse);
      } catch (FormatException) {
        // Failing to convert will trip a validation rule
        return value;
      }
    }

  }

}
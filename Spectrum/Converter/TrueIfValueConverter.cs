using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace Spectrum {

  class TrueIfValueConverter<T> : IValueConverter where T : IComparable {

    private T value;

    // Note that to be bidirectional, values must be unique
    public TrueIfValueConverter(T value) {
      this.value = value;
    }

    public object Convert(
      object value,
      Type targetType,
      object parameter,
      System.Globalization.CultureInfo culture
    ) {
      if (!(value is T)) {
        return value;
      }
      T castValue = (T)value;
      return castValue.CompareTo(this.value) == 0;
    }

    public object ConvertBack(
      object value,
      Type targetType,
      object parameter,
      System.Globalization.CultureInfo culture
    ) {
      if (!(value is bool)) {
        return value;
      }
      bool castValue = (bool)value;
      if (castValue) {
        return this.value;
      }
      return value;
    }

  }

}
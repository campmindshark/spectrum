using System;
using System.Globalization;
using System.Windows.Data;

namespace Spectrum {

  // Configuration stores brightness as 0..1; operator-facing readouts use the
  // percentage people expect. Sliders remain bound to the normalized value.
  public sealed class NormalizedPercentConverter : IValueConverter {

    public object Convert(
      object value,
      Type targetType,
      object parameter,
      CultureInfo culture
    ) {
      if (value == null) {
        return "—";
      }
      return (System.Convert.ToDouble(value, CultureInfo.InvariantCulture) * 100)
        .ToString("0", culture) + "%";
    }

    public object ConvertBack(
      object value,
      Type targetType,
      object parameter,
      CultureInfo culture
    ) => Binding.DoNothing;
  }
}

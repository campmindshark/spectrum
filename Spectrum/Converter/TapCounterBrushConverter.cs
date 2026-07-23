using System;
using System.Windows.Data;
using System.Windows.Media;

namespace Spectrum {

  // Maps BeatBroadcaster.TapTempoActive (true while a tap-tempo sequence is in
  // progress) to the tap button's foreground brush: green while tapping, black
  // once the sequence concludes. The brush lives here rather than in Base so that
  // Base carries no WPF dependency.
  class TapCounterBrushConverter : IValueConverter {

    public object Convert(
      object value,
      Type targetType,
      object parameter,
      System.Globalization.CultureInfo culture
    ) {
      return (value is bool active && active)
        ? Brushes.ForestGreen
        : Brushes.Black;
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

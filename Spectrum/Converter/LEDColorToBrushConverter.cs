using System;
using System.Windows.Data;
using System.Windows.Media;
using Spectrum.Base;
using MediaColor = System.Windows.Media.Color;

namespace Spectrum {

  // Renders one palette slot's swatch in the named-preset list: given an LEDColor
  // (a slot of a stored DomePalette), produces its brush. A gradient slot => a
  // left-to-right LinearGradientBrush; a single-color slot => a solid brush; a
  // null slot => transparent. Presets are immutable snapshots, so no live-update
  // machinery is needed — unlike the live-palette rows, which use
  // GradientPreviewConverter bound to their pickers.
  public class LEDColorToBrushConverter : IValueConverter {

    public object Convert(
      object value,
      Type targetType,
      object parameter,
      System.Globalization.CultureInfo culture
    ) {
      if (!(value is LEDColor color)) {
        return Brushes.Transparent;
      }
      MediaColor start = FromRgb(color.Color1);
      if (!color.IsGradient) {
        return new SolidColorBrush(start);
      }
      return new LinearGradientBrush(start, FromRgb(color.Color2), 0);
    }

    public object ConvertBack(
      object value,
      Type targetType,
      object parameter,
      System.Globalization.CultureInfo culture
    ) {
      throw new NotSupportedException();
    }

    private static MediaColor FromRgb(int rgb) {
      return MediaColor.FromRgb(
        (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb
      );
    }
  }

}

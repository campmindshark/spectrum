using System;
using System.Windows.Data;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace Spectrum {

  // Renders the live-palette row preview: given the two ColorPicker.SelectedColor
  // values (start, end), produces the brush that slot paints with. Two colors =>
  // a left-to-right LinearGradientBrush (matching LEDColor's start-to-end
  // gradient); start only => a solid brush; neither => transparent (an empty
  // slot, the "no color" hole the render path tolerates). Bound via a
  // MultiBinding straight to the row's two pickers, so it updates live as the VJ
  // edits a color — no palette PropertyChanged needed.
  public class GradientPreviewConverter : IMultiValueConverter {

    public object Convert(
      object[] values,
      Type targetType,
      object parameter,
      System.Globalization.CultureInfo culture
    ) {
      MediaColor? start = AsColor(values != null && values.Length > 0 ? values[0] : null);
      MediaColor? end = AsColor(values != null && values.Length > 1 ? values[1] : null);
      if (!start.HasValue) {
        return Brushes.Transparent;
      }
      if (!end.HasValue) {
        return new SolidColorBrush(start.Value);
      }
      return new LinearGradientBrush(start.Value, end.Value, 0);
    }

    public object[] ConvertBack(
      object value,
      Type[] targetTypes,
      object parameter,
      System.Globalization.CultureInfo culture
    ) {
      throw new NotSupportedException();
    }

    private static MediaColor? AsColor(object value) {
      return value is MediaColor color ? color : (MediaColor?)null;
    }
  }

}

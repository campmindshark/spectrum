using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace Spectrum {

  /// <summary>
  /// A minimal color swatch control: it renders the selected color and opens
  /// the system color dialog when clicked. This replaces Xceed's ColorPicker
  /// so the app carries no third-party WPF toolkit dependency. The
  /// SelectedColor dependency property mirrors Xceed's (a nullable Color that
  /// binds two-way by default) so existing bindings keep working unchanged.
  /// The media color type is aliased because the Spectrum namespace has its
  /// own internal Color class that would otherwise shadow it.
  /// </summary>
  public partial class ColorPicker : UserControl {

    public static readonly DependencyProperty SelectedColorProperty =
      DependencyProperty.Register(
        nameof(SelectedColor),
        typeof(MediaColor?),
        typeof(ColorPicker),
        new FrameworkPropertyMetadata(
          null,
          FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
          OnSelectedColorChanged
        )
      );

    public MediaColor? SelectedColor {
      get { return (MediaColor?)this.GetValue(SelectedColorProperty); }
      set { this.SetValue(SelectedColorProperty, value); }
    }

    public ColorPicker() {
      this.InitializeComponent();
      this.UpdateSwatch();
    }

    private static void OnSelectedColorChanged(
      DependencyObject d,
      DependencyPropertyChangedEventArgs e
    ) {
      ((ColorPicker)d).UpdateSwatch();
    }

    private void UpdateSwatch() {
      MediaColor? color = this.SelectedColor;
      if (color.HasValue) {
        this.swatch.Background = new SolidColorBrush(color.Value);
        this.emptyMarker.Visibility = Visibility.Collapsed;
      } else {
        this.swatch.Background = Brushes.White;
        this.emptyMarker.Visibility = Visibility.Visible;
      }
    }

    private void SwatchClicked(object sender, MouseButtonEventArgs e) {
      var dialog = new System.Windows.Forms.ColorDialog() {
        FullOpen = true,
      };
      if (this.SelectedColor.HasValue) {
        MediaColor current = this.SelectedColor.Value;
        dialog.Color = System.Drawing.Color.FromArgb(
          current.R, current.G, current.B
        );
      }
      if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
        this.SelectedColor = MediaColor.FromRgb(
          dialog.Color.R, dialog.Color.G, dialog.Color.B
        );
      }
    }

  }

}

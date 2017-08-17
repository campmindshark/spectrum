using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Spectrum.Base;
using System.ComponentModel;
using Xceed.Wpf.Toolkit;

namespace Spectrum {

  public partial class MidiHUDWindow : Window {

    private Configuration config;

    public MidiHUDWindow(Configuration config) {
      this.InitializeComponent();
      this.config = config;
      this.config.PropertyChanged += ConfigUpdated;
      this.logBox.Document = new FlowDocument();
      this.logBox.ScrollToVerticalOffset(this.logBox.ExtentHeight);
      this.InitializeBindings();
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (!String.Equals(e.PropertyName, "midiLog")) {
        return;
      }
      MidiLogMessage[] newMessages = this.config.midiLog.DequeueAllMessages();
      this.Dispatcher.Invoke(() => {
        bool isScrolledToEnd = this.logBox.VerticalOffset >=
          this.logBox.ExtentHeight - this.logBox.ActualHeight;
        int messagesToRemove = newMessages.Length
          + this.logBox.Document.Blocks.Count
          - ObservableMidiLog.bufferSize;
        for (int i = 0; i < messagesToRemove; i++) {
          this.logBox.Document.Blocks.Remove(
            this.logBox.Document.Blocks.FirstBlock
          );
        }
        foreach (var logMessage in newMessages) {
          StringBuilder timeBuilder = new StringBuilder();
          timeBuilder.Append('[');
          timeBuilder.Append(logMessage.time.ToShortDateString());
          timeBuilder.Append(' ');
          timeBuilder.Append(logMessage.time.ToLongTimeString());
          timeBuilder.Append("] ");
          Run timeRun = new Run(timeBuilder.ToString());
          timeRun.Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 255));
          Run messageRun = new Run(logMessage.message);
          Paragraph paragraph = new Paragraph();
          paragraph.Inlines.Add(timeRun);
          paragraph.Inlines.Add(messageRun);
          paragraph.Margin = new Thickness(0);
          this.logBox.Document.Blocks.Add(paragraph);
        }
        if (isScrolledToEnd) {
          this.logBox.ScrollToVerticalOffset(this.logBox.ExtentHeight);
        }
      });
    }

    private void InitializeBindings() {
      var colorConverter = new ColorConverter();
      this.Bind("[0,0]", this.color0_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[0,1]", this.color0_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[0]", this.domeCC0, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[1,0]", this.color1_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[1,1]", this.color1_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[1]", this.domeCC1, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[2,0]", this.color2_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[2,1]", this.color2_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[2]", this.domeCC2, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[3,0]", this.color3_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[3,1]", this.color3_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[3]", this.domeCC3, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[4,0]", this.color4_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[4,1]", this.color4_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[4]", this.domeCC4, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[5,0]", this.color5_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[5,1]", this.color5_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[5]", this.domeCC5, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[6,0]", this.color6_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[6,1]", this.color6_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[6]", this.domeCC6, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[7,0]", this.color7_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[7,1]", this.color7_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[7]", this.domeCC7, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[8,0]", this.color8_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[8,1]", this.color8_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[8]", this.domeCC8, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[9,0]", this.color9_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[9,1]", this.color9_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[9]", this.domeCC9, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[10,0]", this.color10_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[10,1]", this.color10_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[10]", this.domeCC10, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[11,0]", this.color11_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[11,1]", this.color11_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[11]", this.domeCC11, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[12,0]", this.color12_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[12,1]", this.color12_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[12]", this.domeCC12, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[13,0]", this.color13_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[13,1]", this.color13_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[13]", this.domeCC13, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[14,0]", this.color14_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[14,1]", this.color14_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[14]", this.domeCC14, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("[15,0]", this.color15_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[15,1]", this.color15_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[15]", this.domeCC15, CheckBox.IsCheckedProperty, BindingMode.TwoWay, null, this.config.colorPalette.computerEnabledColors);
      this.Bind("TapCounterBrush", this.tapTempoButton, Button.ForegroundProperty, BindingMode.OneWay, null, this.config.beatBroadcaster);
      this.Bind("TapCounterText", this.tapTempoButton, Button.ContentProperty, BindingMode.OneWay, null, this.config.beatBroadcaster);
      this.Bind("BPMString", this.bpmLabel, Label.ContentProperty, BindingMode.OneWay, null, this.config.beatBroadcaster);
      this.Bind("domeVolumeRotationSpeed", this.domePrimaryRotationSpeed, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<double, ComboBoxItem>(new Dictionary<double, ComboBoxItem> { [0] = this.dprs0, [0.125] = this.dprs1, [0.25] = this.dprs2, [0.5] = this.dprs3, [1.0] = this.dprs4, [2.0] = this.dprs5, [4.0] = this.dprs6 }, true));
      this.Bind("domeGradientSpeed", this.domeSecondaryRotationSpeed, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<double, ComboBoxItem>(new Dictionary<double, ComboBoxItem> { [0] = this.dsrs0, [0.125] = this.dsrs1, [0.25] = this.dsrs2, [0.5] = this.dsrs3, [1.0] = this.dsrs4, [2.0] = this.dsrs5, [4.0] = this.dsrs6 }, true));
      this.Bind("stageTracerSpeed", this.stagePrimaryRotationSpeed, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<double, ComboBoxItem>(new Dictionary<double, ComboBoxItem> { [0] = this.sprs0, [0.125] = this.sprs1, [0.25] = this.sprs2, [0.5] = this.sprs3, [1.0] = this.sprs4, [2.0] = this.sprs5, [4.0] = this.sprs6 }, true));
      this.Bind("flashSpeed", this.flashRotationSpeed, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<double, ComboBoxItem>(new Dictionary<double, ComboBoxItem> { [0] = this.frs0, [0.03125] = this.frs1, [0.0625] = this.frs2, [0.125] = this.frs3, [0.25] = this.frs4, [0.5] = this.frs5, [1.0] = this.frs6 }, true));
    }

    private void Bind(
      string configPath,
      FrameworkElement element,
      DependencyProperty property,
      BindingMode mode = BindingMode.TwoWay,
      IValueConverter converter = null,
      object source = null
    ) {
      var binding = new System.Windows.Data.Binding(configPath);
      binding.Source = source != null ? source : this.config;
      binding.Mode = mode;
      if (converter != null) {
        binding.Converter = converter;
      }
      element.SetBinding(property, binding);
    }

    private void TapTempoButtonClicked(object sender, RoutedEventArgs e) {
      this.config.beatBroadcaster.AddTap();
    }

    private void ClearTempoButtonClicked(object sender, RoutedEventArgs e) {
      this.config.beatBroadcaster.Reset();
    }

  }

}
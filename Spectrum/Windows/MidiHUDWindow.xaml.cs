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
      this.Bind("[1,0]", this.color1_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[1,1]", this.color1_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[2,0]", this.color2_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[2,1]", this.color2_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[3,0]", this.color3_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[3,1]", this.color3_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[4,0]", this.color4_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[4,1]", this.color4_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[5,0]", this.color5_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[5,1]", this.color5_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[6,0]", this.color6_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[6,1]", this.color6_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[7,0]", this.color7_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[7,1]", this.color7_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[8,0]", this.color8_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[8,1]", this.color8_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[9,0]", this.color9_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[9,1]", this.color9_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[10,0]", this.color10_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[10,1]", this.color10_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[11,0]", this.color11_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[11,1]", this.color11_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[12,0]", this.color12_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[12,1]", this.color12_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[13,0]", this.color13_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[13,1]", this.color13_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[14,0]", this.color14_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[14,1]", this.color14_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[15,0]", this.color15_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[15,1]", this.color15_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[16,0]", this.color16_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[16,1]", this.color16_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[17,0]", this.color17_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[17,1]", this.color17_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[18,0]", this.color18_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[18,1]", this.color18_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[19,0]", this.color19_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[19,1]", this.color19_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[20,0]", this.color20_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[20,1]", this.color20_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[21,0]", this.color21_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[21,1]", this.color21_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[22,0]", this.color22_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[22,1]", this.color22_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[23,0]", this.color23_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[23,1]", this.color23_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[24,0]", this.color24_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[24,1]", this.color24_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[25,0]", this.color25_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[25,1]", this.color25_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[26,0]", this.color26_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[26,1]", this.color26_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[27,0]", this.color27_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[27,1]", this.color27_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[28,0]", this.color28_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[28,1]", this.color28_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[29,0]", this.color29_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[29,1]", this.color29_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[30,0]", this.color30_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[30,1]", this.color30_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[31,0]", this.color31_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[31,1]", this.color31_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[32,0]", this.color32_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[32,1]", this.color32_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[33,0]", this.color33_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[33,1]", this.color33_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[34,0]", this.color34_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[34,1]", this.color34_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[35,0]", this.color35_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[35,1]", this.color35_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[36,0]", this.color36_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[36,1]", this.color36_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[37,0]", this.color37_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[37,1]", this.color37_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[38,0]", this.color38_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[38,1]", this.color38_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[39,0]", this.color39_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[39,1]", this.color39_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[40,0]", this.color40_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[40,1]", this.color40_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[41,0]", this.color41_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[41,1]", this.color41_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[42,0]", this.color42_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[42,1]", this.color42_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[43,0]", this.color43_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[43,1]", this.color43_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[44,0]", this.color44_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[44,1]", this.color44_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[45,0]", this.color45_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[45,1]", this.color45_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[46,0]", this.color46_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[46,1]", this.color46_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[47,0]", this.color47_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[47,1]", this.color47_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[48,0]", this.color48_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[48,1]", this.color48_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[49,0]", this.color49_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[49,1]", this.color49_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[50,0]", this.color50_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[50,1]", this.color50_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[51,0]", this.color51_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[51,1]", this.color51_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[52,0]", this.color52_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[52,1]", this.color52_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[53,0]", this.color53_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[53,1]", this.color53_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[54,0]", this.color54_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[54,1]", this.color54_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[55,0]", this.color55_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[55,1]", this.color55_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[56,0]", this.color56_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[56,1]", this.color56_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[57,0]", this.color57_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[57,1]", this.color57_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[58,0]", this.color58_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[58,1]", this.color58_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[59,0]", this.color59_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[59,1]", this.color59_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[60,0]", this.color60_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[60,1]", this.color60_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[61,0]", this.color61_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[61,1]", this.color61_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[62,0]", this.color62_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[62,1]", this.color62_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[63,0]", this.color63_0, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("[63,1]", this.color63_1, ColorPicker.SelectedColorProperty, BindingMode.TwoWay, colorConverter, this.config.colorPalette);
      this.Bind("TapCounterBrush", this.tapTempoButton, Button.ForegroundProperty, BindingMode.OneWay, null, this.config.beatBroadcaster);
      this.Bind("TapCounterText", this.tapTempoButton, Button.ContentProperty, BindingMode.OneWay, null, this.config.beatBroadcaster);
      this.Bind("BPMString", this.bpmLabel, Label.ContentProperty, BindingMode.OneWay, null, this.config.beatBroadcaster);
      this.Bind("domeVolumeRotationSpeed", this.domePrimaryRotationSpeed, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<double, ComboBoxItem>(new Dictionary<double, ComboBoxItem> { [0] = this.dprs0, [0.125] = this.dprs1, [0.25] = this.dprs2, [0.5] = this.dprs3, [1.0] = this.dprs4, [2.0] = this.dprs5, [4.0] = this.dprs6 }, true));
      this.Bind("domeGradientSpeed", this.domeSecondaryRotationSpeed, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<double, ComboBoxItem>(new Dictionary<double, ComboBoxItem> { [0] = this.dsrs0, [0.125] = this.dsrs1, [0.25] = this.dsrs2, [0.5] = this.dsrs3, [1.0] = this.dsrs4, [2.0] = this.dsrs5, [4.0] = this.dsrs6 }, true));
      this.Bind("stageTracerSpeed", this.stagePrimaryRotationSpeed, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<double, ComboBoxItem>(new Dictionary<double, ComboBoxItem> { [0] = this.sprs0, [0.125] = this.sprs1, [0.25] = this.sprs2, [0.5] = this.sprs3, [1.0] = this.sprs4, [2.0] = this.sprs5, [4.0] = this.sprs6 }, true));
      this.Bind("flashSpeed", this.flashRotationSpeed, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<double, ComboBoxItem>(new Dictionary<double, ComboBoxItem> { [0] = this.frs0, [0.5] = this.frs1, [1] = this.frs2, [2] = this.frs3, [4] = this.frs4, [8] = this.frs5, [16] = this.frs6, [32] = this.frs7 }, true));
      this.Bind("colorPaletteIndex", this.colorPalette1, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(0));
      this.Bind("colorPaletteIndex", this.colorPalette2, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(1));
      this.Bind("colorPaletteIndex", this.colorPalette3, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(2));
      this.Bind("colorPaletteIndex", this.colorPalette4, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(3));
      this.Bind("colorPaletteIndex", this.colorPalette5, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(4));
      this.Bind("colorPaletteIndex", this.colorPalette6, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(5));
      this.Bind("colorPaletteIndex", this.colorPalette7, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(6));
      this.Bind("colorPaletteIndex", this.colorPalette8, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(7));
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
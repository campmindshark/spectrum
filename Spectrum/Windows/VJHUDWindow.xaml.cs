using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Spectrum.Base;

namespace Spectrum {

  public partial class VJHUDWindow : Window {

    private readonly Configuration config;
    // The live tempo the tap button drives (owned by the Operator, not part of
    // Configuration).
    private readonly BeatBroadcaster beat;
    private DomeLayersController domeLayersController;
    private DomeScenesController domeScenesController;
    private DomePalettesController domePalettesController;

    public VJHUDWindow(Configuration config, BeatBroadcaster beat) {
      this.InitializeComponent();
      this.config = config;
      this.beat = beat;
      this.InitializeBindings();
    }

    private void InitializeBindings() {
      // The eight live-palette rows edit one of the eight palette banks
      // (colorPalette slots bank*8 .. bank*8+7); the bank selector picks which.
      // Bind the initial bank now, and rebind when the selector changes.
      this.BindLivePalette(this.paletteBankSelector.SelectedIndex);
      this.paletteBankSelector.SelectionChanged += (s, e) =>
        this.BindLivePalette(this.paletteBankSelector.SelectedIndex);
      this.Bind(nameof(this.beat.TapTempoActive), this.tapTempoButton, Button.ForegroundProperty, BindingMode.OneWay, new TapCounterBrushConverter(), this.beat);
      this.Bind(nameof(this.beat.TapCounterText), this.tapTempoButton, Button.ContentProperty, BindingMode.OneWay, null, this.beat);
      this.Bind(nameof(this.beat.BPMString), this.bpmLabel, Label.ContentProperty, BindingMode.OneWay, null, this.beat);

      this.domeLayersController = new DomeLayersController(
        this.config, this.domeLayersItemsControl, this.domeAddLayerButton);
      this.domeScenesController = new DomeScenesController(
        this.config, this.domeScenesCombo, this.domeSceneNameBox,
        this.domeSceneSaveButton, this.domeSceneLoadButton,
        this.domeSceneDeleteButton);
      this.domePalettesController = new DomePalettesController(
        this.config, this.palettePresetList, this.palettePresetNameBox,
        this.palettePresetSaveButton, this.palettePresetApplyButton,
        this.palettePresetDeleteButton,
        () => this.paletteBankSelector.SelectedIndex);
      // Per-visualizer tuning (radial size, ripple steps, ...) is edited in the
      // generic per-layer param rows above; only cross-layer state keeps a
      // dedicated control here.
      this.Bind(nameof(this.config.domeGlobalFadeSpeed), this.domeGlobalFadeSpeedSlider, Slider.ValueProperty);
      this.Bind(nameof(this.config.domeGlobalFadeSpeed), this.domeGlobalFadeSpeedLabel, Label.ContentProperty);
      this.Bind(nameof(this.config.domeGlobalHueSpeed), this.domeGlobalHueSpeedSlider, Slider.ValueProperty);
      this.Bind(nameof(this.config.domeGlobalHueSpeed), this.domeGlobalHueSpeedLabel, Label.ContentProperty);
      this.Bind(nameof(this.config.flashSpeed), this.flashRotationSpeed, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<double, ComboBoxItem>(new Dictionary<double, ComboBoxItem> { [0] = this.frs0, [0.5] = this.frs1, [1] = this.frs2, [2] = this.frs3, [4] = this.frs4, [8] = this.frs5, [16] = this.frs6, [32] = this.frs7 }, true));
      this.Bind(nameof(this.config.beatInput), this.tempoSelectorHuman, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(0));
      this.Bind(nameof(this.config.beatInput), this.tempoSelectorMadmom, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(1));
      this.Bind(nameof(this.config.beatInput), this.tempoSelectorLink, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(2));
    }

    // (Re)bind the eight live-palette rows (16 pickers) to the given bank's
    // slots. SetBinding replaces any previous binding on each picker, so calling
    // this on a bank switch repoints the pickers and pulls the new bank's colors.
    private void BindLivePalette(int bank) {
      if (bank < 0) {
        bank = 0;
      }
      var colorConverter = new ColorConverter();
      int baseSlot = bank * PaletteService.LiveSlots;
      for (int row = 0; row < PaletteService.LiveSlots; row++) {
        for (int whichColor = 0; whichColor < 2; whichColor++) {
          var picker = (ColorPicker)this.FindName($"livecolor{row}_{whichColor}");
          this.Bind(
            $"[{baseSlot + row},{whichColor}]",
            picker,
            ColorPicker.SelectedColorProperty,
            BindingMode.TwoWay,
            colorConverter,
            this.config.colorPalette
          );
        }
      }
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

    private void SliderStarted(object sender, DragStartedEventArgs e) {
      MainWindow.LoadingConfig = true;
    }

    private void SliderCompleted(object sender, DragCompletedEventArgs e) {
      MainWindow.LoadingConfig = false;
    }

    private void TapTempoButtonClicked(object sender, RoutedEventArgs e) {
      this.config.beatInput = 0;
      this.beat.AddTap();
    }

    private void ClearTempoButtonClicked(object sender, RoutedEventArgs e) {
      this.beat.Reset();
    }

    private void OrientationCalibrationClicked(object sender, RoutedEventArgs e) {
      this.config.orientationCalibrate = true;
    }

    private void OrientationDeviceSpotlightChanged(object sender, TextChangedEventArgs e) {
      short deviceId;
      if (System.Int16.TryParse(orientationDeviceSpotlightInput.Text, out deviceId)) {
        this.config.orientationDeviceSpotlight = deviceId;
      }
    }
  }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Spectrum.Base;

namespace Spectrum {

  public partial class VJHUDWindow : Window {

    private GridLength expandedControlRailWidth = new GridLength(300);

    private readonly Configuration config;
    // The live tempo the tap button drives (owned by the Operator, not part of
    // Configuration).
    private readonly BeatBroadcaster beat;
    // The orientation-device source behind the compact wand-spotlight panel.
    private readonly OrientationInput orientation;
    private DomeLayersController domeLayersController;
    private DomeScenesController domeScenesController;
    private DomePalettesController domePalettesController;

    // Backing collection for the wand-spotlight ListView, reconciled in place
    // each poll (keyed by device id, kept sorted) so the list doesn't flicker.
    private readonly ObservableCollection<WandRow> wandRows =
      new ObservableCollection<WandRow>();
    private DispatcherTimer wandTimer;
    // Set while UpdateSpotlightSelection pushes state into the radios, so the
    // radios' own checked-handlers don't write config back (which would recurse).
    private bool suppressSpotlightWrites;

    public VJHUDWindow(
      Configuration config, BeatBroadcaster beat, OrientationInput orientation) {
      this.InitializeComponent();
      this.config = config;
      this.beat = beat;
      this.orientation = orientation;
      this.InitializeBindings();
      this.InitializeWandPanel();
    }

    private void ToggleControlRail(object sender, RoutedEventArgs e) {
      if (this.controlRailColumn.Width.Value > 0) {
        this.expandedControlRailWidth = this.controlRailColumn.Width;
        this.controlRailColumn.Width = new GridLength(0);
        this.controlRailSplitter.Visibility = Visibility.Collapsed;
        this.toggleControlRailButton.Content = "Show setup controls";
      } else {
        this.controlRailColumn.Width =
          this.expandedControlRailWidth.Value > 0
            ? this.expandedControlRailWidth
            : new GridLength(300);
        this.controlRailSplitter.Visibility = Visibility.Visible;
        this.toggleControlRailButton.Content = "Hide setup controls";
      }
    }

    private void ToggleShortcutReference(object sender, RoutedEventArgs e) {
      this.shortcutReference.Visibility =
        this.shortcutReference.Visibility == Visibility.Visible
          ? Visibility.Collapsed
          : Visibility.Visible;
    }

    private void VJHUDKeyDown(object sender, KeyEventArgs e) {
      if (e.Key == Key.F1) {
        this.ToggleShortcutReference(sender, e);
        e.Handled = true;
        return;
      }
      // Text entry and native selectors keep every key they normally use.
      if (Keyboard.FocusedElement is TextBox ||
          Keyboard.FocusedElement is ComboBox) {
        return;
      }
      switch (e.Key) {
        case Key.T:
          this.TapTempoButtonClicked(sender, e);
          e.Handled = true;
          break;
        case Key.C:
          this.ClearTempoButtonClicked(sender, e);
          e.Handled = true;
          break;
        case Key.A:
          this.domeAddLayerButton.RaiseEvent(
            new RoutedEventArgs(ButtonBase.ClickEvent));
          e.Handled = true;
          break;
      }
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
        this.config, this.domeLayersItemsControl, this.domeAddLayerButton,
        this.domeCollapseAllLayersButton, this.domeExpandAllLayersButton);
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

    // ---- Compact wand-spotlight panel ---------------------------------------

    // The mini port of the web "Wand status" panel: one row per connected
    // orientation device (ID / Type / Motion / Quality) with a per-row spotlight
    // radio, plus the "all wands" / "idle" radios above. Polls OrientationInput's
    // thread-safe snapshots on a timer (the device set has no change event), and
    // keeps every radio a reflection of config.orientationDeviceSpotlight.
    private void InitializeWandPanel() {
      this.wandSpotlightList.ItemsSource = this.wandRows;
      this.spotlightAllWands.Checked += (s, e) => this.SetSpotlight(-1);
      this.spotlightIdle.Checked += (s, e) => this.SetSpotlight(-2);
      // Reflect changes made elsewhere (web surface, or OrientationInput clearing
      // a vanished spotlight to -1 on its own thread) back into the radios.
      this.config.PropertyChanged += this.ConfigPropertyChanged;
      this.Loaded += this.WandPanelLoaded;
      this.Closed += this.WandPanelClosed;
    }

    private void WandPanelLoaded(object sender, RoutedEventArgs e) {
      // OrientationInput times a silent device out after ~1s, so a few hundred ms
      // keeps the list responsive without busy-polling — matching WandStatusWindow.
      this.wandTimer =
        new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
      this.wandTimer.Tick += this.RefreshWands;
      this.wandTimer.Start();
      this.RefreshWands(null, null);
    }

    private void WandPanelClosed(object sender, EventArgs e) {
      if (this.wandTimer != null) {
        this.wandTimer.Stop();
        this.wandTimer.Tick -= this.RefreshWands;
        this.wandTimer = null;
      }
      this.config.PropertyChanged -= this.ConfigPropertyChanged;
    }

    private void ConfigPropertyChanged(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == nameof(this.config.orientationDeviceSpotlight)) {
        // The write can originate off the UI thread (OrientationInput's timeout
        // path), so marshal before touching the radios.
        this.Dispatcher.BeginInvoke(
          (Action)(() => this.UpdateSpotlightSelection()));
      }
    }

    private void RefreshWands(object sender, EventArgs e) {
      var snapshot = this.orientation.DevicesSnapshot();
      var statsSnapshot = this.orientation.ConnectionStatsSnapshot();

      // Reconcile in place (keyed by id, kept sorted) so radio state and the
      // ListView's scroll position survive each poll.
      foreach (var id in this.wandRows.Select(r => r.DeviceId).ToList()) {
        if (!snapshot.ContainsKey(id)) {
          this.wandRows.Remove(this.wandRows.First(r => r.DeviceId == id));
        }
      }
      foreach (var kvp in snapshot.OrderBy(kvp => kvp.Key)) {
        statsSnapshot.TryGetValue(kvp.Key, out var deviceStats);
        var existing = this.wandRows.FirstOrDefault(r => r.DeviceId == kvp.Key);
        if (existing == null) {
          int insertAt = this.wandRows.Count(r => r.DeviceId < kvp.Key);
          var row = new WandRow(kvp.Key, kvp.Value, deviceStats);
          row.SpotlightRequested = this.SetSpotlight;
          this.wandRows.Insert(insertAt, row);
        } else {
          existing.Update(kvp.Value, deviceStats);
        }
      }

      int moving = snapshot.Values.Count(d => d.isMoving);
      this.wandSpotlightSummary.Text = this.wandRows.Count == 0
        ? "No wands connected."
        : this.wandRows.Count + (this.wandRows.Count == 1 ? " wand" : " wands") +
          " connected, " + moving + " moving.";

      this.UpdateSpotlightSelection();
    }

    // Asks to make deviceId the spotlight (-1 all wands, -2 idle, else a device).
    // The resulting config change flows back through ConfigPropertyChanged /
    // RefreshWands to refresh every radio, so this only writes.
    private void SetSpotlight(int deviceId) {
      if (this.suppressSpotlightWrites) {
        return;
      }
      this.config.orientationDeviceSpotlight = deviceId;
    }

    // Points every radio at the current spotlight value. "All wands" is the
    // catch-all for any non-idle state with no matching connected wand (the
    // default, or a spotlight id whose device isn't listed) — mirroring the web
    // panel. Guarded so the programmatic checks here don't loop back into writes.
    private void UpdateSpotlightSelection() {
      int spotlight = this.config.orientationDeviceSpotlight;
      this.suppressSpotlightWrites = true;
      try {
        bool spotWand = false;
        foreach (var row in this.wandRows) {
          bool selected = row.DeviceId == spotlight;
          row.IsSpotlight = selected;
          spotWand |= selected;
        }
        this.spotlightIdle.IsChecked = !spotWand && spotlight == -2;
        this.spotlightAllWands.IsChecked = !spotWand && spotlight != -2;
      } finally {
        this.suppressSpotlightWrites = false;
      }
    }
  }
}

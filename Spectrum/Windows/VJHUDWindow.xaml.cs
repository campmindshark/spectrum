using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Spectrum.Base;
using System.ComponentModel;
using System.Windows.Controls.Primitives;

namespace Spectrum {

  public partial class VJHUDWindow : Window {

    private class LevelDriverPresetEntry {
      public string Name { get; set; }
      public LevelDriverSource Source { get; set; }
      public string SourceName {
        get {
          if (this.Source == LevelDriverSource.Audio) {
            return "Audio";
          } else if (this.Source == LevelDriverSource.Midi) {
            return "MIDI";
          } else {
            throw new Exception("Invalid LevelDriverSource!");
          }
        }
      }
    }

    private readonly Configuration config;
    private string currentlyEditingLevelDriverPreset;
    private readonly Dictionary<LevelDriverSource, ComboBox[]> channelComboBoxes;
    private DomeLayersController domeLayersController;

    public VJHUDWindow(Configuration config) {
      this.InitializeComponent();
      this.config = config;
      this.config.PropertyChanged += ConfigUpdated;
      this.logBox.Document = new FlowDocument();
      this.logBox.ScrollToVerticalOffset(this.logBox.ExtentHeight);
      this.InitializeBindings();
      this.channelComboBoxes = this.InitializeChannelComboBoxes();
      this.LoadLevelDriverPresets();
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
          timeRun.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 0, 255));
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
      for (int paletteIndex = 0; paletteIndex < 64; paletteIndex++) {
        for (int whichColor = 0; whichColor < 2; whichColor++) {
          var picker = (ColorPicker)this.FindName(
            $"color{paletteIndex}_{whichColor}"
          );
          this.Bind(
            $"[{paletteIndex},{whichColor}]",
            picker,
            ColorPicker.SelectedColorProperty,
            BindingMode.TwoWay,
            colorConverter,
            this.config.colorPalette
          );
        }
      }
      this.Bind("TapCounterBrush", this.tapTempoButton, Button.ForegroundProperty, BindingMode.OneWay, null, this.config.beatBroadcaster);
      this.Bind("TapCounterText", this.tapTempoButton, Button.ContentProperty, BindingMode.OneWay, null, this.config.beatBroadcaster);
      this.Bind("BPMString", this.bpmLabel, Label.ContentProperty, BindingMode.OneWay, null, this.config.beatBroadcaster);

      this.domeLayersController = new DomeLayersController(
        this.config, this.domeLayersItemsControl, this.domeAddLayerButton);
      this.Bind("domeVolumeRotationSpeed", this.domeVolumeRotationSpeed, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<double, ComboBoxItem>(new Dictionary<double, ComboBoxItem> { [0] = this.dprs0, [0.125] = this.dprs1, [0.25] = this.dprs2, [0.5] = this.dprs3, [1.0] = this.dprs4, [2.0] = this.dprs5, [4.0] = this.dprs6 }, true));
      this.Bind("domeGradientSpeed", this.domeGradientSpeed, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<double, ComboBoxItem>(new Dictionary<double, ComboBoxItem> { [0] = this.dsrs0, [0.125] = this.dsrs1, [0.25] = this.dsrs2, [0.5] = this.dsrs3, [1.0] = this.dsrs4, [2.0] = this.dsrs5, [4.0] = this.dsrs6 }, true));
      this.Bind("domeRadialCenterSpeed", this.domeRadialCenterSpeed, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<double, ComboBoxItem>(new Dictionary<double, ComboBoxItem> { [0] = this.rcs0, [0.125] = this.rcs1, [0.25] = this.rcs2, [0.5] = this.rcs3, [1.0] = this.rcs4, [2.0] = this.rcs5, [4.0] = this.rcs6 }, true));

      this.Bind("domeGlobalFadeSpeed", this.domeGlobalFadeSpeedSlider, Slider.ValueProperty);
      this.Bind("domeGlobalFadeSpeed", this.domeGlobalFadeSpeedLabel, Label.ContentProperty);
      this.Bind("domeGlobalHueSpeed", this.domeGlobalHueSpeedSlider, Slider.ValueProperty);
      this.Bind("domeGlobalHueSpeed", this.domeGlobalHueSpeedLabel, Label.ContentProperty);
      this.Bind("domeTwinkleDensity", this.domeTwinkleDensitySlider, Slider.ValueProperty);
      this.Bind("domeTwinkleDensity", this.domeTwinkleDensityLabel, Label.ContentProperty);
      this.Bind("domeRippleCDStep", this.domeRippleCDStepSlider, Slider.ValueProperty);
      this.Bind("domeRippleCDStep", this.domeRippleCDStepLabel, Label.ContentProperty);
      this.Bind("domeRippleStep", this.domeRippleStepSlider, Slider.ValueProperty);
      this.Bind("domeRippleStep", this.domeRippleStepLabel, Label.ContentProperty);
      this.Bind("domeRadialEffect", this.domeRadialEffect, ComboBox.SelectedIndexProperty, BindingMode.TwoWay);
      this.Bind("domeRadialSize", this.domeRadialSizeSlider, Slider.ValueProperty);
      this.Bind("domeRadialSize", this.domeRadialSizeLabel, Label.ContentProperty);
      this.Bind("domeRadialFrequency", this.domeRadialFrequencySlider, Slider.ValueProperty);
      this.Bind("domeRadialFrequency", this.domeRadialFrequencyLabel, Label.ContentProperty);
      this.Bind("domeRadialCenterAngle", this.domeRadialCenterAngleSlider, Slider.ValueProperty);
      this.Bind("domeRadialCenterAngle", this.domeRadialCenterAngleLabel, Label.ContentProperty);
      this.Bind("domeRadialCenterDistance", this.domeRadialCenterDistanceSlider, Slider.ValueProperty);
      this.Bind("domeRadialCenterDistance", this.domeRadialCenterDistanceLabel, Label.ContentProperty);



      this.Bind("flashSpeed", this.flashRotationSpeed, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<double, ComboBoxItem>(new Dictionary<double, ComboBoxItem> { [0] = this.frs0, [0.5] = this.frs1, [1] = this.frs2, [2] = this.frs3, [4] = this.frs4, [8] = this.frs5, [16] = this.frs6, [32] = this.frs7 }, true));
      this.Bind("colorPaletteIndex", this.colorPalette1, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(0));
      this.Bind("colorPaletteIndex", this.colorPalette2, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(1));
      this.Bind("colorPaletteIndex", this.colorPalette3, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(2));
      this.Bind("colorPaletteIndex", this.colorPalette4, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(3));
      this.Bind("colorPaletteIndex", this.colorPalette5, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(4));
      this.Bind("colorPaletteIndex", this.colorPalette6, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(5));
      this.Bind("colorPaletteIndex", this.colorPalette7, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(6));
      this.Bind("colorPaletteIndex", this.colorPalette8, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(7));
      this.Bind("beatInput", this.tempoSelectorHuman, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(0));
      this.Bind("beatInput", this.tempoSelectorMadmom, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(1));
      this.Bind("beatInput", this.tempoSelectorLink, RadioButton.IsCheckedProperty, BindingMode.TwoWay, new TrueIfValueConverter<int>(2));
      this.Bind("orientationShowContours", this.orientationContours, CheckBox.IsCheckedProperty);
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

    private Dictionary<LevelDriverSource, ComboBox[]> InitializeChannelComboBoxes() {
      return new Dictionary<LevelDriverSource, ComboBox[]> {
        {
          LevelDriverSource.Audio,
          new ComboBox[] {
            this.channel0Audio,
            this.channel1Audio,
            this.channel2Audio,
            this.channel3Audio,
            this.channel4Audio,
            this.channel5Audio,
            this.channel6Audio,
            this.channel7Audio,
          }
        },
        {
          LevelDriverSource.Midi,
          new ComboBox[] {
            this.channel0Midi,
            this.channel1Midi,
            this.channel2Midi,
            this.channel3Midi,
            this.channel4Midi,
            this.channel5Midi,
            this.channel6Midi,
            this.channel7Midi,
          }
        },
      };
    }

    private void TapTempoButtonClicked(object sender, RoutedEventArgs e) {
      this.config.beatInput = 0;
      this.config.beatBroadcaster.AddTap();
    }

    private void ClearTempoButtonClicked(object sender, RoutedEventArgs e) {
      this.config.beatBroadcaster.Reset();
    }

    private void LoadLevelDriverPresets() {
      this.levelDriverPresetList.Items.Clear();
      foreach (var pair in this.config.levelDriverPresets) {
        var levelDriverPreset = pair.Value;
        this.levelDriverPresetList.Items.Add(new LevelDriverPresetEntry() {
          Name = levelDriverPreset.Name,
          Source = levelDriverPreset.Source,
        });
      }
      foreach (var comboBoxPair in this.channelComboBoxes) {
        for (int i = 0; i < comboBoxPair.Value.Length; i++) {
          this.ResetItemsOfChannelComboBox(
            comboBoxPair.Value[i],
            i,
            comboBoxPair.Key
          );
        }
      }
    }

    private void ResetItemsOfChannelComboBox(
      ComboBox box,
      int i, // box index, 0-7
      LevelDriverSource levelDriverSource
    ) {
      string currentName = null;
      if (
        levelDriverSource == LevelDriverSource.Audio &&
        this.config.channelToAudioLevelDriverPreset.ContainsKey(i)
      ) {
        currentName = this.config.channelToAudioLevelDriverPreset[i];
      } else if (
        levelDriverSource == LevelDriverSource.Midi &&
        this.config.channelToMidiLevelDriverPreset.ContainsKey(i)
      ) {
        currentName = this.config.channelToMidiLevelDriverPreset[i];
      }

      box.Items.Clear();
      int boxItemIndex = 0;
      int currentIndex = -1;
      foreach (var levelDriverPair in this.config.levelDriverPresets) {
        ILevelDriverPreset config = levelDriverPair.Value;
        if (config.Source != levelDriverSource) {
          continue;
        }
        box.Items.Add(config.Name);
        if (currentName != null && currentName == config.Name) {
          currentIndex = boxItemIndex;
        }
        boxItemIndex++;
      }
      box.SelectedIndex = currentIndex;
    }

    private void LevelDriverPresetListSelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (this.currentlyEditingLevelDriverPreset != null) {
        this.LevelDriverCancelEditClicked(null, null);
      }
      this.levelDriverDeletePreset.IsEnabled = this.levelDriverPresetList.SelectedIndex >= 0;
      this.levelDriverEditPreset.IsEnabled = this.levelDriverPresetList.SelectedIndex >= 0;
    }

    private void LevelDriverEditClicked(object sender, RoutedEventArgs e) {
      if (this.levelDriverPresetList.SelectedIndex < 0) {
        return;
      }
      var currentItem = (LevelDriverPresetEntry)this.levelDriverPresetList.SelectedItem;
      var levelDriverConfig = this.config.levelDriverPresets[currentItem.Name];
      this.currentlyEditingLevelDriverPreset = currentItem.Name;

      this.levelDriverMutateLabel.Content = "Edit level driver preset";
      this.levelDriverSavePresetButton.Content = "Save";
      this.levelDriverCancelEditButton.Visibility = Visibility.Visible;
      this.levelDriverName.Text = levelDriverConfig.Name;
      this.levelDriverName.Focus();
      this.levelDriverName.SelectionStart = this.levelDriverName.Text.Length;
      this.levelDriverName.SelectionLength = 0;

      this.levelDriverSource.SelectedIndex = (byte)levelDriverConfig.Source;
      this.levelDriverSource.IsEnabled = false;
      if (levelDriverConfig.Source == LevelDriverSource.Audio) {
        var config = (AudioLevelDriverPreset)levelDriverConfig;
        this.levelDriverAudioFilterRangeStart.Text = config.FilterRangeStart.ToString();
        this.levelDriverAudioFilterRangeEnd.Text = config.FilterRangeEnd.ToString();
      } else if (levelDriverConfig.Source == LevelDriverSource.Midi) {
        var config = (MidiLevelDriverPreset)levelDriverConfig;
        this.levelDriverMidiAttack.Text = config.AttackTime.ToString();
        this.levelDriverMidiPeak.Text = config.PeakLevel.ToString();
        this.levelDriverMidiDecay.Text = config.DecayTime.ToString();
        this.levelDriverMidiSustain.Text = config.SustainLevel.ToString();
        this.levelDriverMidiRelease.Text = config.ReleaseTime.ToString();
      }
    }

    private void LevelDriverDeleteClicked(object sender, RoutedEventArgs e) {
      int index = this.levelDriverPresetList.SelectedIndex;
      if (index < 0) {
        return;
      }
      var currentItem = (LevelDriverPresetEntry)this.levelDriverPresetList.SelectedItem;
      var levelDriverConfig = this.config.levelDriverPresets[currentItem.Name];

      var newLevelDriverPresets = new Dictionary<string, ILevelDriverPreset>(this.config.levelDriverPresets);
      newLevelDriverPresets.Remove(levelDriverConfig.Name);
      this.config.levelDriverPresets = newLevelDriverPresets;
      this.levelDriverPresetList.Items.RemoveAt(index);

      var currentDict =
        this.GetChannelToPresetDictionary(levelDriverConfig.Source);
      Dictionary<int, string> nextDict =
        new Dictionary<int, string>(currentDict);
      bool changed = false;
      foreach (var pair in currentDict) {
        if (pair.Value == levelDriverConfig.Name) {
          nextDict.Remove(pair.Key);
          changed = true;
        }
      }
      if (changed) {
        this.SetChannelToPresetDictionary(levelDriverConfig.Source, nextDict);
      }

      var comboBoxesToUpdate = this.channelComboBoxes[levelDriverConfig.Source];
      for (int i = 0; i < comboBoxesToUpdate.Length; i++) {
        ComboBox box = comboBoxesToUpdate[i];
        this.ResetItemsOfChannelComboBox(box, i, levelDriverConfig.Source);
      }
    }

    private Dictionary<int, string> GetChannelToPresetDictionary(
      LevelDriverSource levelDriverSource
    ) {
      if (levelDriverSource == LevelDriverSource.Audio) {
        return this.config.channelToAudioLevelDriverPreset;
      } else if (levelDriverSource == LevelDriverSource.Midi) {
        return this.config.channelToMidiLevelDriverPreset;
      } else {
        throw new Exception("unknown LevelDriverSource!");
      }
    }

    private void SetChannelToPresetDictionary(
      LevelDriverSource levelDriverSource,
      Dictionary<int, string> channelToPresetDictionary
    ) {
      if (levelDriverSource == LevelDriverSource.Audio) {
        this.config.channelToAudioLevelDriverPreset = channelToPresetDictionary;
      } else if (levelDriverSource == LevelDriverSource.Midi) {
        this.config.channelToMidiLevelDriverPreset = channelToPresetDictionary;
      } else {
        throw new Exception("unknown LevelDriverSource!");
      }
    }

    private void LevelDriverSourceChanged(object sender, SelectionChangedEventArgs e) {
      this.levelDriverAudioPresetConfig.Visibility = this.levelDriverSource.SelectedIndex == 0
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.levelDriverMidiPresetConfig.Visibility = this.levelDriverSource.SelectedIndex == 1
        ? Visibility.Visible
        : Visibility.Collapsed;
    }

    private void LevelDriverCancelEditClicked(object sender, RoutedEventArgs e) {
      if (this.currentlyEditingLevelDriverPreset == null) {
        return;
      }

      this.currentlyEditingLevelDriverPreset = null;

      this.levelDriverMutateLabel.Content = "Add level driver preset";
      this.levelDriverSavePresetButton.Content = "Add preset";
      this.levelDriverCancelEditButton.Visibility = Visibility.Collapsed;
      this.levelDriverName.Text = "";
      this.levelDriverSource.SelectedIndex = -1;
      this.levelDriverSource.IsEnabled = true;

      this.levelDriverAudioFilterRangeStart.Text = "";
      this.levelDriverAudioFilterRangeEnd.Text = "";
      this.levelDriverMidiAttack.Text = "";
      this.levelDriverMidiPeak.Text = "";
      this.levelDriverMidiDecay.Text = "";
      this.levelDriverMidiSustain.Text = "";
      this.levelDriverMidiRelease.Text = "";
    }

    private bool MidiPresetNameExists(string name) {
      foreach (var pair in this.config.levelDriverPresets) {
        var thisName = pair.Value.Name;
        if (thisName == name && thisName != this.currentlyEditingLevelDriverPreset) {
          return true;
        }
      }
      return false;
    }


    private void LevelDriverSaveClicked(object sender, RoutedEventArgs e) {
      if (this.levelDriverSource.SelectedIndex == -1) {
        this.levelDriverSource.Focus();
        return;
      }
      var newName = this.levelDriverName.Text.Trim();
      if (String.IsNullOrEmpty(newName) || this.MidiPresetNameExists(newName)) {
        this.levelDriverName.Text = "";
        this.levelDriverName.Focus();
        return;
      }

      string editing = this.currentlyEditingLevelDriverPreset;
      ILevelDriverPreset newPreset;
      if (this.levelDriverSource.SelectedIndex == 0) {
        double startValue, endValue;
        try {
          startValue = Convert.ToDouble(this.levelDriverAudioFilterRangeStart.Text.Trim());
        } catch (Exception) {
          this.levelDriverAudioFilterRangeStart.Text = "";
          this.levelDriverAudioFilterRangeStart.Focus();
          return;
        }
        try {
          endValue = Convert.ToDouble(this.levelDriverAudioFilterRangeEnd.Text.Trim());
        } catch (Exception) {
          this.levelDriverAudioFilterRangeEnd.Text = "";
          this.levelDriverAudioFilterRangeEnd.Focus();
          return;
        }
        if (endValue < startValue) {
          this.levelDriverAudioFilterRangeEnd.Text = "";
          this.levelDriverAudioFilterRangeEnd.Focus();
          return;
        }
        newPreset = new AudioLevelDriverPreset() {
          Name = newName,
          FilterRangeStart = startValue,
          FilterRangeEnd = endValue,
        };
      } else if (this.levelDriverSource.SelectedIndex == 1) {
        int attack, decay, release;
        double peak, sustain;
        try {
          attack = Convert.ToInt32(this.levelDriverMidiAttack.Text.Trim());
        } catch (Exception) {
          this.levelDriverMidiAttack.Text = "";
          this.levelDriverMidiAttack.Focus();
          return;
        }
        try {
          peak = Convert.ToDouble(this.levelDriverMidiPeak.Text.Trim());
        } catch (Exception) {
          this.levelDriverMidiPeak.Text = "";
          this.levelDriverMidiPeak.Focus();
          return;
        }
        try {
          decay = Convert.ToInt32(this.levelDriverMidiDecay.Text.Trim());
        } catch (Exception) {
          this.levelDriverMidiDecay.Text = "";
          this.levelDriverMidiDecay.Focus();
          return;
        }
        try {
          sustain = Convert.ToDouble(this.levelDriverMidiSustain.Text.Trim());
        } catch (Exception) {
          this.levelDriverMidiSustain.Text = "";
          this.levelDriverMidiSustain.Focus();
          return;
        }
        try {
          release = Convert.ToInt32(this.levelDriverMidiRelease.Text.Trim());
        } catch (Exception) {
          this.levelDriverMidiRelease.Text = "";
          this.levelDriverMidiRelease.Focus();
          return;
        }
        newPreset = new MidiLevelDriverPreset() {
          Name = newName,
          AttackTime = attack,
          PeakLevel = peak,
          DecayTime = decay,
          SustainLevel = sustain,
          ReleaseTime = release,
        };
      } else {
        return;
      }

      var newLevelDriverPresets = new Dictionary<string, ILevelDriverPreset>(this.config.levelDriverPresets);
      newLevelDriverPresets[newName] = newPreset;
      if (editing != null && newName != editing) {
        newLevelDriverPresets.Remove(editing);
      }
      this.config.levelDriverPresets = newLevelDriverPresets;

      if (this.levelDriverSource.SelectedIndex == 0) {
        this.levelDriverAudioFilterRangeStart.Text = "";
        this.levelDriverAudioFilterRangeEnd.Text = "";
      } else if (this.levelDriverSource.SelectedIndex == 1) {
        this.levelDriverMidiAttack.Text = "";
        this.levelDriverMidiPeak.Text = "";
        this.levelDriverMidiDecay.Text = "";
        this.levelDriverMidiSustain.Text = "";
        this.levelDriverMidiRelease.Text = "";
      }
      this.levelDriverSource.SelectedIndex = -1;
      this.levelDriverSource.IsEnabled = true;
      this.levelDriverName.Text = "";

      var entry = new LevelDriverPresetEntry() {
        Name = newName,
        Source = newPreset.Source,
      };
      if (editing != null) {
        this.levelDriverPresetList.Items[this.levelDriverPresetList.SelectedIndex] = entry;
      } else {
        this.levelDriverPresetList.Items.Add(entry);
      }

      if (editing != null && newName != editing) {
        var currentDict = this.GetChannelToPresetDictionary(newPreset.Source);
        Dictionary<int, string> nextDict =
          new Dictionary<int, string>(currentDict);
        bool changed = false;
        foreach (var pair in currentDict) {
          if (pair.Value == editing) {
            nextDict[pair.Key] = newName;
            changed = true;
          }
        }
        if (changed) {
          this.SetChannelToPresetDictionary(newPreset.Source, nextDict);
        }
      }

      var comboBoxesToUpdate = this.channelComboBoxes[newPreset.Source];
      for (int i = 0; i < comboBoxesToUpdate.Length; i++) {
        ComboBox box = comboBoxesToUpdate[i];
        this.ResetItemsOfChannelComboBox(box, i, newPreset.Source);
      }
    }

    private void LevelDriverAudioFilterRangeStartValueLostFocus(object sender, RoutedEventArgs e) {
      try {
        double filterRangeStart = Convert.ToDouble(this.levelDriverAudioFilterRangeStart.Text.Trim());
        if (filterRangeStart < 0.0 || filterRangeStart > 1.0) {
          this.levelDriverAudioFilterRangeStart.Text = "";
        }
      } catch (Exception) {
        this.levelDriverAudioFilterRangeStart.Text = "";
      }
    }

    private void LevelDriverAudioFilterRangeEndValueLostFocus(object sender, RoutedEventArgs e) {
      try {
        double filterRangeEnd = Convert.ToDouble(this.levelDriverAudioFilterRangeEnd.Text.Trim());
        if (filterRangeEnd < 0.0 || filterRangeEnd > 1.0) {
          this.levelDriverAudioFilterRangeEnd.Text = "";
        }
      } catch (Exception) {
        this.levelDriverAudioFilterRangeEnd.Text = "";
      }
    }

    private void LevelDriverMidiAttackLostFocus(object sender, RoutedEventArgs e) {
      try {
        int attack = Convert.ToInt32(this.levelDriverMidiAttack.Text.Trim());
        if (attack < 0) {
          this.levelDriverMidiAttack.Text = "";
        }
      } catch (Exception) {
        this.levelDriverMidiAttack.Text = "";
      }
    }

    private void LevelDriverMidiPeakLostFocus(object sender, RoutedEventArgs e) {
      try {
        double peak = Convert.ToDouble(this.levelDriverMidiPeak.Text.Trim());
        if (peak < 0.0 || peak > 1.0) {
          this.levelDriverMidiPeak.Text = "";
        }
      } catch (Exception) {
        this.levelDriverMidiPeak.Text = "";
      }
    }

    private void LevelDriverMidiDecayLostFocus(object sender, RoutedEventArgs e) {
      try {
        int decay = Convert.ToInt32(this.levelDriverMidiDecay.Text.Trim());
        if (decay < 0) {
          this.levelDriverMidiDecay.Text = "";
        }
      } catch (Exception) {
        this.levelDriverMidiDecay.Text = "";
      }
    }

    private void LevelDriverMidiSustainLostFocus(object sender, RoutedEventArgs e) {
      try {
        double sustain = Convert.ToDouble(this.levelDriverMidiSustain.Text.Trim());
        if (sustain < 0.0 || sustain > 1.0) {
          this.levelDriverMidiSustain.Text = "";
        }
      } catch (Exception) {
        this.levelDriverMidiSustain.Text = "";
      }
    }

    private void LevelDriverMidiReleaseLostFocus(object sender, RoutedEventArgs e) {
      try {
        int release = Convert.ToInt32(this.levelDriverMidiRelease.Text.Trim());
        if (release < 0) {
          this.levelDriverMidiRelease.Text = "";
        }
      } catch (Exception) {
        this.levelDriverMidiRelease.Text = "";
      }
    }

    private void ChannelAudioSelectionChanged(object sender, SelectionChangedEventArgs e) {
      ComboBox box = (ComboBox)sender;
      int channelIndex = Convert.ToInt32((string)box.Tag);
      this.config.channelToAudioLevelDriverPreset[channelIndex] = (string)box.SelectedItem;
    }

    private void ChannelMidiSelectionChanged(object sender, SelectionChangedEventArgs e) {
      ComboBox box = (ComboBox)sender;
      int channelIndex = Convert.ToInt32((string)box.Tag);
      this.config.channelToMidiLevelDriverPreset[channelIndex] = (string)box.SelectedItem;
    }

    private void OrientationCalibrationClicked(object sender, RoutedEventArgs e) {
      this.config.orientationCalibrate = true;
    }

    private void OrientationDeviceSpotlightChanged(object sender, TextChangedEventArgs e) {
      short deviceId;
      if (Int16.TryParse(orientationDeviceSpotlightInput.Text, out deviceId)) {
        this.config.orientationDeviceSpotlight = deviceId;
      }
    }
  }
}

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Spectrum.Audio;
using XSerializer;
using System.IO;
using Spectrum.MIDI;
using System.Windows.Data;
using Xceed.Wpf.Toolkit;
using System.Collections.Generic;
using System.Windows.Media;
using Spectrum.Base;
using System.ComponentModel;
using System.Reflection;

namespace Spectrum {

  using Timer = System.Windows.Forms.Timer;

  public partial class MainWindow : Window {

    private class MidiDeviceEntry {

      public int DeviceID { get; set; }

      public string DeviceName {
        get {
          if (MidiInput.DeviceCount <= this.DeviceID) {
            return "< DISCONNECTED >";
          }
          return MidiInput.GetDeviceName(this.DeviceID);
        }
      }

      public string PresetName { get; set; }

    }

    private class MidiBindingEntry {
      public string BindingName { get; set; }
      public string BindingTypeName { get; set; }
    }

    private static HashSet<string> configPropertiesToRebootOn = new HashSet<string>() {
      "audioInputInSeparateThread",
      "huesOutputInSeparateThread",
      "ledBoardOutputInSeparateThread",
      "midiInputInSeparateThread",
      "domeOutputInSeparateThread",
      "whyFireOutputInSeparateThread",
      "barOutputInSeparateThread",
      "stageOutputInSeparateThread",
    };
    private static HashSet<string> configPropertiesIgnored = new HashSet<string>() {
      "operatorFPS",
      "domeTeensyFPS1",
      "domeTeensyFPS2",
      "domeTeensyFPS3",
      "domeTeensyFPS4",
      "domeTeensyFPS5",
      "domeBeagleboneOPCFPS",
      "domeBeagleboneCAMPFPS",
      "boardTeensyFPS",
      "boardBeagleboneOPCFPS",
      "boardBeagleboneCAMPFPS",
      "barTeensyFPS",
      "barBeagleboneOPCFPS",
      "barBeagleboneCAMPFPS",
      "stageTeensyFPS1",
      "stageTeensyFPS2",
      "stageBeagleboneOPCFPS",
      "stageBeagleboneCAMPFPS",
      "beatBroadcaster",
      "domeCommandQueue",
      "barCommandQueue",
      "stageCommandQueue",
    };

    private Operator op;
    private SpectrumConfiguration config;
    private bool loadingConfig = false;
    private int[] audioDeviceIndices;
    private List<int> midiDeviceIndices;
    private List<int> midiPresetIndices;
    private DomeSimulatorWindow domeSimulatorWindow;
    private BarSimulatorWindow barSimulatorWindow;
    private StageSimulatorWindow stageSimulatorWindow;
    private MidiHUDWindow midiHUDWindow;
    private int? currentlyEditingPreset = null;
    private int? currentlyEditingBinding = null;
    private Timer configSaveTimer = null;

    public MainWindow() {
      this.InitializeComponent();

      new HotKey(Key.Q, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.OemTilde, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.R, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.OemPeriod, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.OemComma, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Left, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Right, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Up, KeyModifier.Alt, this.OnHotKeyHandler);
      new HotKey(Key.Down, KeyModifier.Alt, this.OnHotKeyHandler);

      this.LoadConfig();
      this.config.PropertyChanged += ConfigUpdated;
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (configPropertiesToRebootOn.Contains(e.PropertyName)) {
        this.op.Reboot();
      }
      if (!configPropertiesIgnored.Contains(e.PropertyName)) {
        this.EventuallySaveConfig();
      }
    }

    private void HandleClose(object sender, EventArgs e) {
      this.op.Enabled = false;
      this.SaveConfig();
    }

    private void SaveConfig() {
      if (this.loadingConfig) {
        return;
      }
      // We keep around the old config in case the new config causes a crash
      if (File.Exists("spectrum_config.xml")) {
        File.Copy("spectrum_config.xml", "spectrum_old_config.xml", true);
      }
      using (
        FileStream stream = new FileStream(
          "spectrum_config.xml",
          FileMode.Create
        )
      ) {
        new XmlSerializer<SpectrumConfiguration>().Serialize(
          stream,
          this.config
        );
      }
    }

    private void EventuallySaveConfig() {
      lock (this.op) {
        if (this.configSaveTimer == null) {
          this.configSaveTimer = new Timer();
          this.configSaveTimer.Interval = 100;
          this.configSaveTimer.Tick += DelayedConfigSave;
          this.configSaveTimer.Start();
        }
      }
    }

    private void DelayedConfigSave(object sender, EventArgs e) {
      lock (this.op) {
        this.SaveConfig();
        this.configSaveTimer.Stop();
        this.configSaveTimer.Dispose();
        this.configSaveTimer = null;
      }
    }

    private void LoadConfig() {
      this.loadingConfig = true;

      if (File.Exists("spectrum_config.xml")) {
        using (FileStream stream = File.OpenRead("spectrum_config.xml")) {
          this.config = new XmlSerializer<SpectrumConfiguration>(
          ).Deserialize(stream);
        }
      }
      if (this.config == null) {
        this.config = new SpectrumConfiguration();
      }
      this.op = new Operator(this.config);

      this.RefreshAudioDevices(null, null);
      this.RefreshLEDBoardPorts(null, null);
      this.RefreshMidiDevices(null, null);
      this.RefreshDomePorts(null, null);
      this.RefreshBarUSBPorts(null, null);
      this.RefreshStageUSBPorts(null, null);
      this.LoadPresets();

      this.Bind("huesEnabled", this.hueEnabled, CheckBox.IsCheckedProperty);
      this.Bind("ledBoardEnabled", this.ledBoardEnabled, CheckBox.IsCheckedProperty);
      this.Bind("midiInputEnabled", this.midiEnabled, CheckBox.IsCheckedProperty);
      this.Bind("audioInputInSeparateThread", this.audioThreadCheckbox, CheckBox.IsCheckedProperty);
      this.Bind("huesOutputInSeparateThread", this.hueThreadCheckbox, CheckBox.IsCheckedProperty);
      this.Bind("ledBoardOutputInSeparateThread", this.ledBoardThreadCheckbox, CheckBox.IsCheckedProperty);
      this.Bind("midiInputInSeparateThread", this.midiThreadCheckbox, CheckBox.IsCheckedProperty);
      this.Bind("domeOutputInSeparateThread", this.domeThreadCheckbox, CheckBox.IsCheckedProperty);
      this.Bind("whyFireOutputInSeparateThread", this.whyFireThreadCheckbox, CheckBox.IsCheckedProperty);
      this.Bind("barOutputInSeparateThread", this.barThreadCheckbox, CheckBox.IsCheckedProperty);
      this.Bind("stageOutputInSeparateThread", this.stageThreadCheckbox, CheckBox.IsCheckedProperty);
      this.Bind("operatorFPS", this.operatorFPSLabel, Label.ContentProperty);
      this.Bind("operatorFPS", this.operatorFPSLabel, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeHardwareSetup", this.domeHardwareSetup, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> { [0] = this.fiveTeensies, [1] = this.beagleboneViaOPC, [2] = this.beagleboneViaCAMP }, true));
      this.Bind("domeHardwareSetup", this.domeTeensies, WrapPanel.VisibilityProperty, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Visible, [1] = Visibility.Collapsed, [2] = Visibility.Collapsed }));
      this.Bind("domeHardwareSetup", this.domeBeagleboneOPCPanel, Grid.VisibilityProperty, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Visible, [2] = Visibility.Collapsed }));
      this.Bind("domeHardwareSetup", this.domeBeagleboneCAMPPanel, Grid.VisibilityProperty, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Collapsed, [2] = Visibility.Visible }));
      this.Bind("domeTeensyFPS1", this.domeTeensyFPS1Label, Label.ContentProperty);
      this.Bind("domeTeensyFPS1", this.domeTeensyFPS1Label, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensyFPS1Label, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensy1, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("domeTeensyFPS2", this.domeTeensyFPS2Label, Label.ContentProperty);
      this.Bind("domeTeensyFPS2", this.domeTeensyFPS2Label, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensyFPS2Label, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensy2, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("domeTeensyFPS3", this.domeTeensyFPS3Label, Label.ContentProperty);
      this.Bind("domeTeensyFPS3", this.domeTeensyFPS3Label, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensyFPS3Label, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensy3, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("domeTeensyFPS4", this.domeTeensyFPS4Label, Label.ContentProperty);
      this.Bind("domeTeensyFPS4", this.domeTeensyFPS4Label, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensyFPS4Label, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensy4, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("domeTeensyFPS5", this.domeTeensyFPS5Label, Label.ContentProperty);
      this.Bind("domeTeensyFPS5", this.domeTeensyFPS5Label, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensyFPS5Label, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("domeOutputInSeparateThread", this.domeTeensy5, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("domeBeagleboneOPCAddress", this.domeBeagleboneOPCHostAndPort, TextBox.TextProperty);
      this.Bind("domeBeagleboneOPCFPS", this.domeBeagleboneOPCFPSLabel, Label.ContentProperty);
      this.Bind("domeBeagleboneOPCFPS", this.domeBeagleboneOPCFPSLabel, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeOutputInSeparateThread", this.domeBeagleboneOPCFPSLabel, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("domeOutputInSeparateThread", this.domeBeagleboneOPCHostAndPort, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("domeBeagleboneCAMPAddress", this.domeBeagleboneCAMPHostAndPort, TextBox.TextProperty);
      this.Bind("domeBeagleboneCAMPFPS", this.domeBeagleboneCAMPFPSLabel, Label.ContentProperty);
      this.Bind("domeBeagleboneCAMPFPS", this.domeBeagleboneCAMPFPSLabel, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("domeOutputInSeparateThread", this.domeBeagleboneCAMPFPSLabel, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("domeOutputInSeparateThread", this.domeBeagleboneCAMPHostAndPort, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("domeTestPattern", this.domeTestPattern, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> { [0] = this.domeTestPatternNone, [1] = this.domeTestPatternFlashColorsByStrut, [2] = this.domeTestPatternIterateThroughStruts }, true));
      this.Bind("boardTeensyFPS", this.boardTeensyFPSLabel, Label.ContentProperty);
      this.Bind("boardTeensyFPS", this.boardTeensyFPSLabel, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("ledBoardOutputInSeparateThread", this.boardTeensyFPSLabel, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("ledBoardOutputInSeparateThread", this.ledBoardUSBPorts, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("boardBeagleboneOPCAddress", this.boardBeagleboneOPCHostAndPort, TextBox.TextProperty);
      this.Bind("boardBeagleboneOPCFPS", this.boardBeagleboneOPCFPSLabel, Label.ContentProperty);
      this.Bind("boardBeagleboneOPCFPS", this.boardBeagleboneOPCFPSLabel, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("ledBoardOutputInSeparateThread", this.boardBeagleboneOPCFPSLabel, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("ledBoardOutputInSeparateThread", this.boardBeagleboneOPCHostAndPort, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("boardBeagleboneCAMPAddress", this.boardBeagleboneCAMPHostAndPort, TextBox.TextProperty);
      this.Bind("boardBeagleboneCAMPFPS", this.boardBeagleboneCAMPFPSLabel, Label.ContentProperty);
      this.Bind("boardBeagleboneCAMPFPS", this.boardBeagleboneCAMPFPSLabel, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("ledBoardOutputInSeparateThread", this.boardBeagleboneCAMPFPSLabel, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("ledBoardOutputInSeparateThread", this.boardBeagleboneCAMPHostAndPort, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("barTeensyFPS", this.barTeensyFPSLabel, Label.ContentProperty);
      this.Bind("barTeensyFPS", this.barTeensyFPSLabel, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("barOutputInSeparateThread", this.barTeensyFPSLabel, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("barOutputInSeparateThread", this.barUSBPorts, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("barBeagleboneOPCAddress", this.barBeagleboneOPCHostAndPort, TextBox.TextProperty);
      this.Bind("barBeagleboneOPCFPS", this.barBeagleboneOPCFPSLabel, Label.ContentProperty);
      this.Bind("barBeagleboneOPCFPS", this.barBeagleboneOPCFPSLabel, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("barOutputInSeparateThread", this.barBeagleboneOPCFPSLabel, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("barOutputInSeparateThread", this.barBeagleboneOPCHostAndPort, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("barBeagleboneCAMPAddress", this.barBeagleboneCAMPHostAndPort, TextBox.TextProperty);
      this.Bind("barBeagleboneCAMPFPS", this.barBeagleboneCAMPFPSLabel, Label.ContentProperty);
      this.Bind("barBeagleboneCAMPFPS", this.barBeagleboneCAMPFPSLabel, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("barOutputInSeparateThread", this.barBeagleboneCAMPFPSLabel, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("barOutputInSeparateThread", this.barBeagleboneCAMPHostAndPort, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("barTestPattern", this.barTestPattern, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> { [0] = this.barTestPatternNone, [1] = this.barTestPatternFlashColors }, true));
      this.Bind("stageTeensyFPS1", this.stageTeensyFPS1Label, Label.ContentProperty);
      this.Bind("stageTeensyFPS1", this.stageTeensyFPS1Label, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("stageTeensyFPS2", this.stageTeensyFPS2Label, Label.ContentProperty);
      this.Bind("stageTeensyFPS2", this.stageTeensyFPS2Label, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("stageOutputInSeparateThread", this.stageTeensyFPS1Label, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("stageOutputInSeparateThread", this.stageTeensyUSBPorts1, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("stageOutputInSeparateThread", this.stageTeensyFPS2Label, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("stageOutputInSeparateThread", this.stageTeensyUSBPorts2, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("stageBeagleboneOPCAddress", this.stageBeagleboneOPCHostAndPort, TextBox.TextProperty);
      this.Bind("stageBeagleboneOPCFPS", this.stageBeagleboneOPCFPSLabel, Label.ContentProperty);
      this.Bind("stageBeagleboneOPCFPS", this.stageBeagleboneOPCFPSLabel, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("stageOutputInSeparateThread", this.stageBeagleboneOPCFPSLabel, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("stageOutputInSeparateThread", this.stageBeagleboneOPCHostAndPort, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("stageBeagleboneCAMPAddress", this.stageBeagleboneCAMPHostAndPort, TextBox.TextProperty);
      this.Bind("stageBeagleboneCAMPFPS", this.stageBeagleboneCAMPFPSLabel, Label.ContentProperty);
      this.Bind("stageBeagleboneCAMPFPS", this.stageBeagleboneCAMPFPSLabel, Label.ForegroundProperty, BindingMode.OneWay, new FPSToBrushConverter());
      this.Bind("stageOutputInSeparateThread", this.stageBeagleboneCAMPFPSLabel, Label.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("stageOutputInSeparateThread", this.stageBeagleboneCAMPHostAndPort, ComboBox.WidthProperty, BindingMode.OneWay, new SpecificValuesConverter<bool, int>(new Dictionary<bool, int> { [false] = 140, [true] = 115 }));
      this.Bind("stageTestPattern", this.stageTestPattern, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> { [0] = this.stageTestPatternNone, [1] = this.stageTestPatternFlashColors }, true));
      this.Bind("hueDelay", this.hueCommandDelay, TextBox.TextProperty);
      this.Bind("hueIdleOnSilent", this.hueIdleOnSilent, CheckBox.IsCheckedProperty);
      this.Bind("hueOverrideIndex", this.hueOverride, ComboBox.SelectedIndexProperty);
      this.Bind("hueOverrideIsCustom", this.hueCustomGrid, Grid.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("hueOverrideIsDisabled", this.hueIdleOnSilent, Grid.VisibilityProperty, BindingMode.OneWay, new BooleanToVisibilityConverter());
      this.Bind("brighten", this.hueCustomBrightness, TextBox.TextProperty);
      this.Bind("sat", this.hueCustomSaturation, TextBox.TextProperty);
      this.Bind("colorslide", this.hueCustomHue, TextBox.TextProperty);
      this.Bind("peakC", this.peakChangeS, Slider.ValueProperty);
      this.Bind("peakC", this.peakChangeL, Label.ContentProperty);
      this.Bind("dropQ", this.dropQuietS, Slider.ValueProperty);
      this.Bind("dropQ", this.dropQuietL, Label.ContentProperty);
      this.Bind("dropT", this.dropChangeS, Slider.ValueProperty);
      this.Bind("dropT", this.dropChangeL, Label.ContentProperty);
      this.Bind("kickQ", this.kickQuietS, Slider.ValueProperty);
      this.Bind("kickQ", this.kickQuietL, Label.ContentProperty);
      this.Bind("kickT", this.kickChangeS, Slider.ValueProperty);
      this.Bind("kickT", this.kickChangeL, Label.ContentProperty);
      this.Bind("snareQ", this.snareQuietS, Slider.ValueProperty);
      this.Bind("snareQ", this.snareQuietL, Label.ContentProperty);
      this.Bind("snareT", this.snareChangeS, Slider.ValueProperty);
      this.Bind("snareT", this.snareChangeL, Label.ContentProperty);
      this.Bind("hueURL", this.hueHubAddress, TextBox.TextProperty);
      this.Bind("hueIndices", this.hueLightIndices, TextBox.TextProperty, BindingMode.TwoWay, new StringJoinConverter());
      this.Bind("boardRowLength", this.ledBoardRowLength, TextBox.TextProperty);
      this.Bind("boardRowsPerStrip", this.ledBoardRowsPerStrip, TextBox.TextProperty);
      this.Bind("boardBrightness", this.ledBoardBrightnessSlider, Slider.ValueProperty);
      this.Bind("boardBrightness", this.ledBoardBrightnessLabel, Label.ContentProperty);
      this.Bind("boardHardwareSetup", this.boardHardwareSetup, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> { [0] = this.boardHardwareSetupTeensy, [1] = this.boardHardwareSetupBeagleboneViaOPC, [2] = this.boardHardwareSetupBeagleboneViaCAMP }, true));
      this.Bind("boardHardwareSetup", this.boardTeensyPanel, WrapPanel.VisibilityProperty, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Visible, [1] = Visibility.Collapsed, [2] = Visibility.Collapsed }));
      this.Bind("boardHardwareSetup", this.boardBeagleboneOPCPanel, Grid.VisibilityProperty, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Visible, [2] = Visibility.Collapsed }));
      this.Bind("boardHardwareSetup", this.boardBeagleboneCAMPPanel, Grid.VisibilityProperty, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Collapsed, [2] = Visibility.Visible }));
      this.Bind("domeEnabled", this.domeEnabled, CheckBox.IsCheckedProperty);
      this.Bind("domeSimulationEnabled", this.domeSimulationEnabled, CheckBox.IsCheckedProperty);
      this.Bind("domeMaxBrightness", this.domeMaxBrightnessSlider, Slider.ValueProperty);
      this.Bind("domeMaxBrightness", this.domeMaxBrightnessLabel, Label.ContentProperty);
      this.Bind("domeBrightness", this.domeBrightnessSlider, Slider.ValueProperty);
      this.Bind("domeBrightness", this.domeBrightnessLabel, Label.ContentProperty);
      this.Bind("domeVolumeAnimationSize", this.domeVolumeAnimationSize, ComboBox.SelectedIndexProperty);
      this.Bind("domeAutoFlashDelay", this.domeAutoFlashDelay, TextBox.TextProperty);
      this.Bind("domeSkipLEDs", this.domeSkipLEDs, TextBox.TextProperty);
      this.Bind("whyFireEnabled", this.whyFireEnabled, CheckBox.IsCheckedProperty);
      this.Bind("whyFireURL", this.whyFireAddress, TextBox.TextProperty);
      this.Bind("barEnabled", this.barEnabled, CheckBox.IsCheckedProperty);
      this.Bind("barSimulationEnabled", this.barSimulationEnabled, CheckBox.IsCheckedProperty);
      this.Bind("barHardwareSetup", this.barHardwareSetup, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> { [0] = this.barHardwareSetupTeensy, [1] = this.barHardwareSetupBeagleboneViaOPC, [2] = this.barHardwareSetupBeagleboneViaCAMP }, true));
      this.Bind("barHardwareSetup", this.barTeensyPanel, WrapPanel.VisibilityProperty, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Visible, [1] = Visibility.Collapsed, [2] = Visibility.Collapsed }));
      this.Bind("barHardwareSetup", this.barBeagleboneOPCPanel, Grid.VisibilityProperty, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Visible, [2] = Visibility.Collapsed }));
      this.Bind("barHardwareSetup", this.barBeagleboneCAMPPanel, Grid.VisibilityProperty, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Collapsed, [2] = Visibility.Visible }));
      this.Bind("barInfinityLength", this.barInfinityLength, TextBox.TextProperty);
      this.Bind("barInfinityWidth", this.barInfiniteWidth, TextBox.TextProperty);
      this.Bind("barRunnerLength", this.barRunnerLength, TextBox.TextProperty);
      this.Bind("barBrightness", this.barBrightnessSlider, Slider.ValueProperty);
      this.Bind("barBrightness", this.barBrightnessLabel, Label.ContentProperty);
      this.Bind("stageEnabled", this.stageEnabled, CheckBox.IsCheckedProperty);
      this.Bind("stageSimulationEnabled", this.stageSimulationEnabled, CheckBox.IsCheckedProperty);
      this.Bind("stageHardwareSetup", this.stageHardwareSetup, ComboBox.SelectedItemProperty, BindingMode.TwoWay, new SpecificValuesConverter<int, ComboBoxItem>(new Dictionary<int, ComboBoxItem> { [0] = this.stageHardwareSetupTwoTeensies, [1] = this.stageHardwareSetupBeagleboneViaOPC, [2] = this.stageHardwareSetupBeagleboneViaCAMP }, true));
      this.Bind("stageHardwareSetup", this.stageTeensyPanel, WrapPanel.VisibilityProperty, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Visible, [1] = Visibility.Collapsed, [2] = Visibility.Collapsed }));
      this.Bind("stageHardwareSetup", this.stageBeagleboneOPCPanel, Grid.VisibilityProperty, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Visible, [2] = Visibility.Collapsed }));
      this.Bind("stageHardwareSetup", this.stageBeagleboneCAMPPanel, Grid.VisibilityProperty, BindingMode.OneWay, new SpecificValuesConverter<int, Visibility>(new Dictionary<int, Visibility> { [0] = Visibility.Collapsed, [1] = Visibility.Collapsed, [2] = Visibility.Visible }));
      this.Bind("stageSideLengths", this.stageSideLengths, TextBox.TextProperty, BindingMode.TwoWay, new StringJoinConverter());
      this.Bind("stageBrightness", this.stageBrightnessSlider, Slider.ValueProperty);
      this.Bind("stageBrightness", this.stageBrightnessLabel, Label.ContentProperty);
      this.Bind("midiHUDEnabled", this.midiHUDEnabled, CheckBox.IsCheckedProperty);

      this.loadingConfig = false;
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
      this.loadingConfig = true;
    }

    private void SliderCompleted(object sender, DragCompletedEventArgs e) {
      this.loadingConfig = false;
    }

    private void OnHotKeyHandler(HotKey hotKey) {
      if (hotKey.Key.Equals(Key.Q)) {
        this.hueOverride.SelectedItem =
          this.hueOverride.SelectedItem == this.hueOverrideOn
            ? this.hueOverrideDisable
            : this.hueOverrideOn;
      } else if (hotKey.Key.Equals(Key.OemTilde)) {
        this.hueOverride.SelectedItem =
          this.hueOverride.SelectedItem == this.hueOverrideOff
            ? this.hueOverrideDisable
            : this.hueOverrideOff;
      } else if (hotKey.Key.Equals(Key.R)) {
        this.hueOverride.SelectedItem =
          this.hueOverride.SelectedItem == this.hueOverrideRed
            ? this.hueOverrideDisable
            : this.hueOverrideRed;
      } else if (hotKey.Key.Equals(Key.OemPeriod)) {
        this.hueCustomBrightness.Text =
          Math.Min(this.config.brighten + 1, 0).ToString();
      } else if (hotKey.Key.Equals(Key.OemComma)) {
        this.hueCustomBrightness.Text =
          Math.Max(this.config.brighten - 1, -4).ToString();
      } else if (hotKey.Key.Equals(Key.Left)) {
        this.hueCustomHue.Text = (this.config.colorslide - 1).ToString();
      } else if (hotKey.Key.Equals(Key.Right)) {
        this.hueCustomHue.Text = (this.config.colorslide + 1).ToString();
      } else if (hotKey.Key.Equals(Key.Up)) {
        this.hueCustomSaturation.Text =
          Math.Min(this.config.sat + 1, 2).ToString();
      } else if (hotKey.Key.Equals(Key.Down)) {
        this.hueCustomSaturation.Text =
          Math.Max(this.config.sat - 1, -2).ToString();
      }
      //config.colorslide = (config.colorslide + 4 + 16) % 16 - 4;??
    }

    private void PowerButtonClicked(object sender, RoutedEventArgs e) {
      if (this.op.Enabled) {
        this.op.Enabled = false;
        this.powerButton.Content = "Go";
      } else {
        this.op.Enabled = true;
        this.powerButton.Content = "Stop";
      }
    }

    private void RefreshAudioDevices(object sender, RoutedEventArgs e) {
      this.op.Enabled = false;
      this.powerButton.Content = "Go";

      int deviceCount = AudioInput.DeviceCount;
      this.audioDeviceIndices = new int[deviceCount];

      this.audioDevices.Items.Clear();
      int itemIndex = 0;
      for (int i = 0; i < deviceCount; i++) {
        if (!AudioInput.IsEnabledInputDevice(i)) {
          continue;
        }
        this.audioDevices.Items.Add(AudioInput.GetDeviceName(i));
        this.audioDeviceIndices[itemIndex++] = i;
      }

      this.audioDevices.SelectedIndex = Array.FindIndex(
        this.audioDeviceIndices,
        i => i == this.config.audioDeviceIndex
      );
    }

    private void AudioInputDeviceChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.audioDevices.SelectedIndex == -1) {
        return;
      }
      this.config.audioDeviceIndex =
        this.audioDeviceIndices[this.audioDevices.SelectedIndex];
      this.op.Reboot();
    }

    private void RefreshLEDBoardPorts(object sender, RoutedEventArgs e) {
      this.ledBoardEnabled.IsChecked = false;

      this.ledBoardUSBPorts.Items.Clear();
      foreach (string portName in System.IO.Ports.SerialPort.GetPortNames()) {
        this.ledBoardUSBPorts.Items.Add(portName);
      }

      this.ledBoardUSBPorts.SelectedValue = this.config.boardTeensyUSBPort;
    }

    private void LEDBoardUSBPortsChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.ledBoardUSBPorts.SelectedIndex == -1) {
        return;
      }
      this.config.boardTeensyUSBPort = this.ledBoardUSBPorts.SelectedItem as string;
      this.op.Reboot();
    }

    // Refresh the list of available devices for the "Add device" panel
    private void RefreshMidiDevices(object sender, RoutedEventArgs e) {
      var currentDevice = this.midiDevices.SelectedItem;

      this.midiDevices.Items.Clear();
      this.midiDeviceIndices = new List<int>();
      for (int i = 0; i < MidiInput.DeviceCount; i++) {
        if (!this.config.midiDevices.ContainsKey(i)) {
          this.midiDevices.Items.Add(MidiInput.GetDeviceName(i));
          this.midiDeviceIndices.Add(i);
        }
      }

      this.midiDevices.SelectedItem = currentDevice;

      // This may change the name of devices with a given device ID, so we
      // regenerate the midiDeviceList
      this.LoadMidiDevices();
    }

    // Refresh the list of active/created "devices" (paired with a preset)
    private void LoadMidiDevices() {
      this.midiDeviceList.Items.Clear();
      foreach (var pair in this.config.midiDevices) {
        this.midiDeviceList.Items.Add(new MidiDeviceEntry {
          DeviceID = pair.Key,
          PresetName = this.config.midiPresets[pair.Value].Name,
        });
      }
    }

    // Only called once, at the start, to populate the parts of the UI that
    // need a list of all presets
    private void LoadPresets() {
      this.midiNewDevicePreset.Items.Clear();
      this.midiPresetIndices = new List<int>();
      foreach (var pair in this.config.midiPresets) {
        var midiPreset = pair.Value;
        this.midiNewDevicePreset.Items.Add(midiPreset.Name);
        this.midiPresetIndices.Add(midiPreset.id);
        this.midiPresetList.Items.Add(midiPreset.Name);
      }
      this.midiNewDevicePreset.Items.Add("New preset");
    }

    private void MidiNewDeviceSelectionChanged(object sender, RoutedEventArgs e) {
      var lastVisibility = this.midiNewDevicePresetNameGrid.Visibility;
      this.midiNewDevicePresetNameGrid.Visibility =
        this.midiNewDevicePreset.SelectedIndex == this.midiPresetIndices.Count
          ? Visibility.Visible
          : Visibility.Collapsed;
      if (
        this.midiNewDevicePresetNameGrid.Visibility != lastVisibility &&
        this.midiNewDevicePresetNameGrid.Visibility == Visibility.Visible
      ) {
        this.midiNewDevicePresetName.Focus();
      }
    }

    private void MidiNewDeviceNewPresetNameLostFocus(object sender, RoutedEventArgs e) {
      var name = this.midiNewDevicePresetName.Text.Trim();
      if (String.IsNullOrEmpty(name)) {
        this.ClearMidiNewDevicePresetName();
      }
    }

    private void MidiNewDeviceNewPresetNameGotFocus(object sender, RoutedEventArgs e) {
      this.midiNewDevicePresetName.Foreground = new SolidColorBrush(Colors.Black);
      this.midiNewDevicePresetName.FontStyle = FontStyles.Normal;
      var name = this.midiNewDevicePresetName.Text.Trim();
      if (String.Equals(name, "New preset name")) {
        this.midiNewDevicePresetName.Text = "";
      }
    }

    private void ClearMidiNewDevicePresetName() {
      this.midiNewDevicePresetName.Text = "New preset name";
      this.midiNewDevicePresetName.Foreground = new SolidColorBrush(Colors.Gray);
      this.midiNewDevicePresetName.FontStyle = FontStyles.Italic;
    }

    private string ValidateNewMidiPresetName(string presetName) {
      var newPresetName = presetName.Trim();
      if (
        String.IsNullOrEmpty(newPresetName) ||
        String.Equals(newPresetName, "New preset name") ||
        this.MidiPresetNameExists(newPresetName)
      ) {
        return null;
      }
      return newPresetName;
    }

    private MidiPreset AddNewMidiPresetWithName(string presetName) {
      var newPresetName = this.ValidateNewMidiPresetName(presetName);
      if (newPresetName == null) {
        return null;
      }
      int newID = this.getNextMidiPresetID();
      var newPreset = new MidiPreset() { id = newID, Name = newPresetName };
      this.AddMidiPreset(newPreset);
      return newPreset;
    }

    private void AddMidiPreset(MidiPreset preset) {
      var newMidiPresets = new Dictionary<int, MidiPreset>(this.config.midiPresets);
      newMidiPresets[preset.id] = preset;
      this.config.midiPresets = newMidiPresets;
      this.midiNewDevicePreset.Items.Insert(
        this.midiNewDevicePreset.Items.Count - 1,
        preset.Name
      );
      this.midiPresetIndices.Add(preset.id);

      this.midiPresetList.Items.Add(preset.Name);
    }

    private int getNextMidiPresetID() {
      int newID = -1;
      foreach (var pair in this.config.midiPresets) {
        if (pair.Key > newID) {
          newID = pair.Key;
        }
      }
      return newID + 1;
    }

    private void MidiAddDeviceClicked(object sender, RoutedEventArgs e) {
      if (this.midiNewDevicePreset.SelectedIndex == -1) {
        this.midiNewDevicePreset.Focus();
        return;
      }
      if (this.midiDevices.SelectedIndex == -1) {
        this.midiDevices.Focus();
        return;
      }
      int presetID;
      string presetName;
      if (this.midiNewDevicePreset.SelectedIndex >= this.midiPresetIndices.Count) {
        // "New preset" was selected
        var result = this.AddNewMidiPresetWithName(this.midiNewDevicePresetName.Text);
        if (result == null) {
          this.midiNewDevicePresetName.Focus();
        }
        presetID = result.id;
        presetName = result.Name;
      } else {
        presetID = this.midiPresetIndices[this.midiNewDevicePreset.SelectedIndex];
        presetName = this.config.midiPresets[presetID].Name;
      }
      var deviceID = this.midiDeviceIndices[this.midiDevices.SelectedIndex];
      this.midiDeviceList.Items.Add(new MidiDeviceEntry {
        DeviceID = deviceID,
        PresetName = presetName,
      });
      var newDevices = new Dictionary<int, int>(this.config.midiDevices);
      newDevices.Add(deviceID, presetID);
      this.config.midiDevices = newDevices;
      this.midiDeviceIndices.RemoveAt(this.midiDevices.SelectedIndex);
      this.midiDevices.Items.RemoveAt(this.midiDevices.SelectedIndex);
      this.midiDevices.SelectedIndex = -1;
      this.midiNewDevicePreset.SelectedIndex = -1;
      this.ClearMidiNewDevicePresetName();
      this.midiNewDevicePresetNameGrid.Visibility = Visibility.Collapsed;

      if (this.midiPresetList.SelectedIndex >= 0) {
        var currentPresetIndex = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
        if (presetID == currentPresetIndex) {
          this.midiDeletePreset.IsEnabled = false;
        }
      }
    }

    // Delete one of the active/created "devices"
    private void MidiDeleteDeviceClicked(object sender, RoutedEventArgs e) {
      var newDevices = new Dictionary<int, int>(this.config.midiDevices);
      MidiDeviceEntry item = (MidiDeviceEntry)this.midiDeviceList.SelectedItem;
      var presetIndex = newDevices[item.DeviceID];
      newDevices.Remove(item.DeviceID);
      this.config.midiDevices = newDevices;
      this.midiDeviceList.Items.RemoveAt(this.midiDeviceList.SelectedIndex);
      this.RefreshMidiDevices(null, null);

      if (this.midiPresetList.SelectedIndex >= 0) {
        var currentPresetIndex = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
        if (
          presetIndex == currentPresetIndex &&
          !this.config.midiDevices.ContainsValue(currentPresetIndex)
        ) {
          this.midiDeletePreset.IsEnabled = true;
        }
      }
    }

    // Take a selected active/created "device" and load its preset into the preset panel
    private void MidiLoadPresetClicked(object sender, RoutedEventArgs e) {
      MidiDeviceEntry item = (MidiDeviceEntry)this.midiDeviceList.SelectedItem;
      this.midiPresetList.SelectedItem = item.PresetName;
    }

    private void MidiDeviceListSelectionChanged(object sender, SelectionChangedEventArgs e) {
      var deviceSelected = this.midiDeviceList.SelectedIndex >= 0;
      this.midiDeleteDevice.IsEnabled = deviceSelected;
      this.midiLoadDevicePreset.IsEnabled = deviceSelected;
    }

    private void MidiAddPresetClicked(object sender, RoutedEventArgs e) {
      if (this.currentlyEditingPreset.HasValue) {
        var newPresetName = this.ValidateNewMidiPresetName(this.midiNewPresetName.Text);
        if (newPresetName == null) {
          this.midiNewPresetName.Focus();
          return;
        }
        // We don't need to reset the whole midiPresets var to trigger observers since nobody
        // cares what presets are named
        this.config.midiPresets[this.currentlyEditingPreset.Value].Name = newPresetName;
        this.SaveConfig();
        var presetIndex = this.midiPresetIndices[this.currentlyEditingPreset.Value];
        this.LoadMidiDevices();
        this.midiPresetList.Items[presetIndex] = newPresetName;
        this.midiNewDevicePreset.Items[presetIndex] = newPresetName;
      } else {
        var result = this.AddNewMidiPresetWithName(this.midiNewPresetName.Text);
        if (result == null) {
          this.midiNewPresetName.Focus();
          return;
        }
      }
      this.ClearMidiNewPresetName();
    }

    private void MidiDeletePresetClicked(object sender, RoutedEventArgs e) {
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      var newPresets = new Dictionary<int, MidiPreset>(this.config.midiPresets);
      newPresets.Remove(presetID);
      this.config.midiPresets = newPresets;
      this.midiPresetIndices.RemoveAt(this.midiPresetList.SelectedIndex);
      this.midiNewDevicePreset.Items.RemoveAt(this.midiPresetList.SelectedIndex);
      this.midiPresetList.Items.RemoveAt(this.midiPresetList.SelectedIndex);
    }

    private void MidiPresetListSelectionChanged(object sender, SelectionChangedEventArgs e) {
      this.midiBindingList.Items.Clear();
      if (this.currentlyEditingPreset.HasValue) {
        this.MidiCancelEditPresetClicked(null, null);
      }
      if (this.currentlyEditingBinding.HasValue) {
        this.MidiCancelEditBindingClicked(null, null);
      }
      if (this.midiPresetList.SelectedIndex < 0) {
        this.midiDeletePreset.IsEnabled = false;
        this.midiClonePreset.IsEnabled = false;
        this.midiRenamePreset.IsEnabled = false;
        this.midiAddBinding.IsEnabled = false;
        return;
      }
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      this.midiDeletePreset.IsEnabled = !this.config.midiDevices.ContainsValue(presetID);
      this.midiClonePreset.IsEnabled = true;
      this.midiRenamePreset.IsEnabled = true;
      this.midiAddBinding.IsEnabled = true;
      foreach (var binding in this.config.midiPresets[presetID].Bindings) {
        ComboBoxItem item = (ComboBoxItem)this.midiBindingType.Items[binding.BindingType];
        this.midiBindingList.Items.Add(new MidiBindingEntry() {
          BindingName = binding.BindingName,
          BindingTypeName = (string)(item.Content),
        });
      }
    }

    private bool MidiPresetNameExists(string name) {
      foreach (var pair in this.config.midiPresets) {
        if (pair.Value.Name == name) {
          return true;
        }
      }
      return false;
    }

    private void MidiClonePresetClicked(object sender, RoutedEventArgs e) {
      if (this.midiPresetList.SelectedIndex < 0) {
        return;
      }
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      MidiPreset clonedPreset = (MidiPreset)this.config.midiPresets[presetID].Clone();
      clonedPreset.id = this.getNextMidiPresetID();

      string newName = clonedPreset.Name + " (clone)";
      int i = 1;
      while (true) {
        if (!this.MidiPresetNameExists(newName)) {
          break;
        }
        newName = clonedPreset.Name + " (clone " + ++i + ")";
      }
      clonedPreset.Name = newName;

      this.AddMidiPreset(clonedPreset);
    }

    private void MidiRenamePresetClicked(object sender, RoutedEventArgs e) {
      if (this.midiPresetList.SelectedIndex < 0) {
        return;
      }
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      this.currentlyEditingPreset = presetID;

      this.midiPresetEditLabel.Content = "Rename preset";
      this.midiNewPresetName.Width = 120;
      this.midiAddPreset.Content = "Save";
      this.midiAddPreset.Margin = new Thickness(0, 0, 55, 0);
      this.midiCancelEditPreset.Visibility = Visibility.Visible;
      this.midiNewPresetName.Text = this.config.midiPresets[presetID].Name;
      this.midiNewPresetName.Focus();
      this.midiNewPresetName.SelectionStart = this.midiNewPresetName.Text.Length;
      this.midiNewPresetName.SelectionLength = 0;
    }

    private void MidiCancelEditPresetClicked(object sender, RoutedEventArgs e) {
      if (!this.currentlyEditingPreset.HasValue) {
        return;
      }
      this.currentlyEditingPreset = null;
      this.midiPresetEditLabel.Content = "Add preset";
      this.midiNewPresetName.Width = 140;
      this.midiAddPreset.Content = "Add preset";
      this.midiAddPreset.Margin = new Thickness(0, 0, 0, 0);
      this.midiCancelEditPreset.Visibility = Visibility.Collapsed;
      this.ClearMidiNewPresetName();
    }

    private void MidiNewPresetNameLostFocus(object sender, RoutedEventArgs e) {
      var name = this.midiNewPresetName.Text.Trim();
      if (String.IsNullOrEmpty(name)) {
        this.ClearMidiNewPresetName();
      }
    }

    private void MidiNewPresetNameGotFocus(object sender, RoutedEventArgs e) {
      this.midiNewPresetName.Foreground = new SolidColorBrush(Colors.Black);
      this.midiNewPresetName.FontStyle = FontStyles.Normal;
      var name = this.midiNewPresetName.Text.Trim();
      if (String.Equals(name, "New preset name")) {
        this.midiNewPresetName.Text = "";
      }
    }

    private void ClearMidiNewPresetName() {
      this.midiNewPresetName.Text = "New preset name";
      this.midiNewPresetName.Foreground = new SolidColorBrush(Colors.Gray);
      this.midiNewPresetName.FontStyle = FontStyles.Italic;
    }

    private void MidiBindingTypeSelectionChanged(object sender, SelectionChangedEventArgs e) {
      this.midiChangeColorBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 0
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.midiTapTempoBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 1
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.midiContinuousKnobBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 2
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.midiDiscreteKnobBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 3
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.midiLogarithmicKnobBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 4
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.midiAdsrLevelDriverBindingPanel.Visibility = this.midiBindingType.SelectedIndex == 5
        ? Visibility.Visible
        : Visibility.Collapsed;
    }

    private static MidiCommandType commandTypeFromIndex(int index) {
      if (index == 0) {
        return MidiCommandType.Knob;
      } else if (index == 1) {
        return MidiCommandType.Program;
      } else if (index == 2) {
        return MidiCommandType.Note;
      }
      throw new Exception("invalid CommandType index " + index);
    }

    private static int indexFromCommandType(MidiCommandType commandType) {
      if (commandType == MidiCommandType.Knob) {
        return 0;
      } else if (commandType == MidiCommandType.Program) {
        return 1;
      } else if (commandType == MidiCommandType.Note) {
        return 2;
      }
      throw new Exception("invalid CommandType " + commandType.ToString());
    }

    private void MidiAddBindingClicked(object sender, RoutedEventArgs e) {
      if (this.midiPresetList.SelectedIndex == -1) {
        this.midiPresetList.Focus();
        return;
      }
      if (this.midiBindingType.SelectedIndex == -1) {
        this.midiBindingType.Focus();
        return;
      }
      var newName = this.midiNewBindingName.Text.Trim();
      if (String.IsNullOrEmpty(newName)) {
        this.midiNewBindingName.Text = "";
        this.midiNewBindingName.Focus();
        return;
      }

      bool editing = this.currentlyEditingBinding.HasValue;
      IMidiBindingConfig newBinding;
      if (this.midiBindingType.SelectedIndex == 0) {
        int indexRangeStart;
        try {
          indexRangeStart = Convert.ToInt32(this.midiChangeColorIndexRangeStart.Text.Trim());
        } catch (Exception) {
          this.midiChangeColorIndexRangeStart.Text = "";
          this.midiChangeColorIndexRangeStart.Focus();
          return;
        }
        newBinding = new ColorPaletteMidiBindingConfig() {
          BindingName = newName,
          indexRangeStart = indexRangeStart,
        };
      } else if (this.midiBindingType.SelectedIndex == 1) {
        if (this.midiTapTempoButtonType.SelectedIndex == -1) {
          this.midiTapTempoButtonType.Focus();
          return;
        }
        var buttonType = commandTypeFromIndex(this.midiTapTempoButtonType.SelectedIndex);
        int buttonIndex;
        try {
          buttonIndex = Convert.ToInt32(this.midiTapTempoButtonIndex.Text.Trim());
        } catch (Exception) {
          this.midiTapTempoButtonIndex.Text = "";
          this.midiTapTempoButtonIndex.Focus();
          return;
        }
        newBinding = new TapTempoMidiBindingConfig() {
          BindingName = newName,
          buttonType = buttonType,
          buttonIndex = buttonIndex,
        };
      } else if (this.midiBindingType.SelectedIndex == 2) {
        string configPropertyName = this.midiContinuousKnobPropertyName.Text.Trim();
        if (String.IsNullOrEmpty(configPropertyName)) {
          this.midiContinuousKnobPropertyName.Text = "";
          this.midiContinuousKnobPropertyName.Focus();
          return;
        }
        int knobIndex;
        try {
          knobIndex = Convert.ToInt32(this.midiContinuousKnobIndex.Text.Trim());
        } catch (Exception) {
          this.midiContinuousKnobIndex.Text = "";
          this.midiContinuousKnobIndex.Focus();
          return;
        }
        double startValue, endValue;
        try {
          startValue = Convert.ToDouble(this.midiContinuousKnobStartValue.Text.Trim());
        } catch (Exception) {
          this.midiContinuousKnobStartValue.Text = "";
          this.midiContinuousKnobStartValue.Focus();
          return;
        }
        try {
          endValue = Convert.ToDouble(this.midiContinuousKnobEndValue.Text.Trim());
        } catch (Exception) {
          this.midiContinuousKnobEndValue.Text = "";
          this.midiContinuousKnobEndValue.Focus();
          return;
        }
        if (endValue < startValue) {
          this.midiContinuousKnobEndValue.Text = "";
          this.midiContinuousKnobEndValue.Focus();
          return;
        }
        newBinding = new ContinuousKnobMidiBindingConfig() {
          BindingName = newName,
          knobIndex = knobIndex,
          configPropertyName = configPropertyName,
          startValue = startValue,
          endValue = endValue,
        };
      } else if (this.midiBindingType.SelectedIndex == 3) {
        string configPropertyName = this.midiDiscreteKnobPropertyName.Text.Trim();
        if (String.IsNullOrEmpty(configPropertyName)) {
          this.midiDiscreteKnobPropertyName.Text = "";
          this.midiDiscreteKnobPropertyName.Focus();
          return;
        }
        int knobIndex, numPossibleValues;
        try {
          knobIndex = Convert.ToInt32(this.midiDiscreteKnobIndex.Text.Trim());
        } catch (Exception) {
          this.midiDiscreteKnobIndex.Text = "";
          this.midiDiscreteKnobIndex.Focus();
          return;
        }
        try {
          numPossibleValues = Convert.ToInt32(this.midiDiscreteKnobNumPossibleValues.Text.Trim());
        } catch (Exception) {
          this.midiDiscreteKnobNumPossibleValues.Text = "";
          this.midiDiscreteKnobNumPossibleValues.Focus();
          return;
        }
        newBinding = new DiscreteKnobMidiBindingConfig() {
          BindingName = newName,
          knobIndex = knobIndex,
          configPropertyName = configPropertyName,
          numPossibleValues = numPossibleValues,
        };
      } else if (this.midiBindingType.SelectedIndex == 4) {
        string configPropertyName = this.midiLogarithmicKnobPropertyName.Text.Trim();
        if (String.IsNullOrEmpty(configPropertyName)) {
          this.midiLogarithmicKnobPropertyName.Text = "";
          this.midiLogarithmicKnobPropertyName.Focus();
          return;
        }
        int knobIndex, numPossibleValues;
        try {
          knobIndex = Convert.ToInt32(this.midiLogarithmicKnobIndex.Text.Trim());
        } catch (Exception) {
          this.midiLogarithmicKnobIndex.Text = "";
          this.midiLogarithmicKnobIndex.Focus();
          return;
        }
        try {
          numPossibleValues = Convert.ToInt32(this.midiLogarithmicKnobNumPossibleValues.Text.Trim());
        } catch (Exception) {
          this.midiLogarithmicKnobNumPossibleValues.Text = "";
          this.midiLogarithmicKnobNumPossibleValues.Focus();
          return;
        }
        double startValue;
        try {
          startValue = Convert.ToDouble(this.midiLogarithmicKnobStartValue.Text.Trim());
        } catch (Exception) {
          this.midiLogarithmicKnobStartValue.Text = "";
          this.midiLogarithmicKnobStartValue.Focus();
          return;
        }
        newBinding = new DiscreteLogarithmicKnobMidiBindingConfig() {
          BindingName = newName,
          knobIndex = knobIndex,
          configPropertyName = configPropertyName,
          numPossibleValues = numPossibleValues,
          startValue = startValue,
        };
      } else if (this.midiBindingType.SelectedIndex == 5) {
        int indexRangeStart;
        try {
          indexRangeStart = Convert.ToInt32(this.midiAdsrLevelDriverIndexRangeStart.Text.Trim());
        } catch (Exception) {
          this.midiAdsrLevelDriverIndexRangeStart.Text = "";
          this.midiAdsrLevelDriverIndexRangeStart.Focus();
          return;
        }
        newBinding = new AdsrLevelDriverMidiBindingConfig() {
          BindingName = newName,
          indexRangeStart = indexRangeStart,
        };
      } else {
        return;
      }

      var newMidiPresets = new Dictionary<int, MidiPreset>(this.config.midiPresets);
      var midiPreset = newMidiPresets[this.midiPresetIndices[this.midiPresetList.SelectedIndex]];
      if (editing) {
        midiPreset.Bindings[this.currentlyEditingBinding.Value] = newBinding;
      } else {
        midiPreset.Bindings.Add(newBinding);
      }
      this.config.midiPresets = newMidiPresets;

      if (this.midiBindingType.SelectedIndex == 0) {
        this.midiChangeColorIndexRangeStart.Text = "";
      } else if (this.midiBindingType.SelectedIndex == 1) {
        this.midiTapTempoButtonType.SelectedIndex = -1;
        this.midiTapTempoButtonIndex.Text = "";
      } else if (this.midiBindingType.SelectedIndex == 2) {
        this.midiContinuousKnobIndex.Text = "";
        this.midiContinuousKnobPropertyName.Text = "";
        this.midiContinuousKnobStartValue.Text = "";
        this.midiContinuousKnobEndValue.Text = "";
      } else if (this.midiBindingType.SelectedIndex == 3) {
        this.midiDiscreteKnobIndex.Text = "";
        this.midiDiscreteKnobPropertyName.Text = "";
        this.midiDiscreteKnobNumPossibleValues.Text = "";
      } else if (this.midiBindingType.SelectedIndex == 4) {
        this.midiLogarithmicKnobIndex.Text = "";
        this.midiLogarithmicKnobPropertyName.Text = "";
        this.midiLogarithmicKnobNumPossibleValues.Text = "";
        this.midiLogarithmicKnobStartValue.Text = "";
      } else if (this.midiBindingType.SelectedIndex == 5) {
        this.midiAdsrLevelDriverIndexRangeStart.Text = "";
      }

      ComboBoxItem item = (ComboBoxItem)this.midiBindingType.SelectedItem;
      string bindingTypeName = (string)item.Content;
      if (editing) {
        int bindingIndex = this.currentlyEditingBinding.Value;
        var entry = (MidiBindingEntry)this.midiBindingList.Items[bindingIndex];
        this.midiBindingList.Items[bindingIndex] = new MidiBindingEntry() {
          BindingName = newName,
          BindingTypeName = entry.BindingTypeName,
        };
      } else {
        this.midiBindingList.Items.Add(new MidiBindingEntry() {
          BindingName = newName,
          BindingTypeName = bindingTypeName,
        });
      }

      this.midiBindingType.SelectedIndex = -1;
      this.midiNewBindingName.Text = "";
    }

    private void MidiBindingListSelectionChanged(object sender, SelectionChangedEventArgs e) {
      var buttonsEnabled = this.midiPresetList.SelectedIndex >= 0 &&
        this.midiBindingList.SelectedIndex >= 0;
      this.midiDeleteBinding.IsEnabled = buttonsEnabled;
      this.midiEditBinding.IsEnabled = buttonsEnabled;
      if (this.currentlyEditingBinding.HasValue) {
        this.MidiCancelEditBindingClicked(null, null);
      }
    }

    private void MidiDeleteBindingClicked(object sender, RoutedEventArgs e) {
      if (this.midiPresetList.SelectedIndex == -1) {
        return;
      }
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      if (this.midiBindingList.SelectedIndex == -1) {
        return;
      }
      var bindingID = this.midiBindingList.SelectedIndex;
      var newMidiPresets = new Dictionary<int, MidiPreset>(this.config.midiPresets);
      newMidiPresets[presetID].Bindings.RemoveAt(bindingID);
      this.config.midiPresets = newMidiPresets;
      this.midiBindingList.Items.RemoveAt(bindingID);
    }

    private void MidiEditBindingClicked(object sender, RoutedEventArgs e) {
      if (
        this.midiPresetList.SelectedIndex < 0 ||
        this.midiBindingList.SelectedIndex < 0
      ) {
        return;
      }
      var presetID = this.midiPresetIndices[this.midiPresetList.SelectedIndex];
      var bindingIndex = this.midiBindingList.SelectedIndex;
      var bindingConfig = this.config.midiPresets[presetID].Bindings[bindingIndex];
      this.currentlyEditingBinding = bindingIndex;

      this.midiBindingEditLabel.Content = "Edit binding";
      this.midiAddBinding.Content = "Save";
      this.midiCancelEditBinding.Visibility = Visibility.Visible;
      this.midiNewBindingName.Text = bindingConfig.BindingName;
      this.midiNewBindingName.Focus();
      this.midiNewBindingName.SelectionStart = this.midiNewBindingName.Text.Length;
      this.midiNewBindingName.SelectionLength = 0;

      this.midiBindingType.SelectedIndex = bindingConfig.BindingType;
      if (bindingConfig.BindingType == 0) {
        var config = (ColorPaletteMidiBindingConfig)bindingConfig;
        this.midiChangeColorIndexRangeStart.Text = config.indexRangeStart.ToString();
      } else if (this.midiBindingType.SelectedIndex == 1) {
        var config = (TapTempoMidiBindingConfig)bindingConfig;
        this.midiTapTempoButtonType.SelectedIndex = indexFromCommandType(config.buttonType);
        this.midiTapTempoButtonIndex.Text = config.buttonIndex.ToString();
      } else if (this.midiBindingType.SelectedIndex == 2) {
        var config = (ContinuousKnobMidiBindingConfig)bindingConfig;
        this.midiContinuousKnobIndex.Text = config.knobIndex.ToString();
        this.midiContinuousKnobPropertyName.Text = config.configPropertyName;
        this.midiContinuousKnobStartValue.Text = config.startValue.ToString();
        this.midiContinuousKnobEndValue.Text = config.endValue.ToString();
      } else if (this.midiBindingType.SelectedIndex == 3) {
        var config = (DiscreteKnobMidiBindingConfig)bindingConfig;
        this.midiDiscreteKnobIndex.Text = config.knobIndex.ToString();
        this.midiDiscreteKnobPropertyName.Text = config.configPropertyName;
        this.midiDiscreteKnobNumPossibleValues.Text = config.numPossibleValues.ToString();
      } else if (this.midiBindingType.SelectedIndex == 4) {
        var config = (DiscreteLogarithmicKnobMidiBindingConfig)bindingConfig;
        this.midiLogarithmicKnobIndex.Text = config.knobIndex.ToString();
        this.midiLogarithmicKnobPropertyName.Text = config.configPropertyName;
        this.midiLogarithmicKnobNumPossibleValues.Text = config.numPossibleValues.ToString();
        this.midiLogarithmicKnobStartValue.Text = config.startValue.ToString();
      } else if (this.midiBindingType.SelectedIndex == 5) {
        var config = (AdsrLevelDriverMidiBindingConfig)bindingConfig;
        this.midiAdsrLevelDriverIndexRangeStart.Text = config.indexRangeStart.ToString();
      }
    }

    private void MidiCancelEditBindingClicked(object sender, RoutedEventArgs e) {
      if (!this.currentlyEditingBinding.HasValue) {
        return;
      }

      this.currentlyEditingBinding = null;
      this.midiBindingEditLabel.Content = "Add binding";
      this.midiAddBinding.Content = "Add binding";
      this.midiCancelEditBinding.Visibility = Visibility.Collapsed;
      this.midiNewBindingName.Text = "";
      this.midiBindingType.SelectedIndex = -1;

      this.midiChangeColorIndexRangeStart.Text = "";

      this.midiTapTempoButtonType.SelectedIndex = -1;
      this.midiTapTempoButtonIndex.Text = "";

      this.midiContinuousKnobIndex.Text = "";
      this.midiContinuousKnobPropertyName.Text = "";
      this.midiContinuousKnobStartValue.Text = "";
      this.midiContinuousKnobEndValue.Text = "";

      this.midiDiscreteKnobIndex.Text = "";
      this.midiDiscreteKnobPropertyName.Text = "";
      this.midiDiscreteKnobNumPossibleValues.Text = "";

      this.midiLogarithmicKnobIndex.Text = "";
      this.midiLogarithmicKnobPropertyName.Text = "";
      this.midiLogarithmicKnobNumPossibleValues.Text = "";
      this.midiLogarithmicKnobStartValue.Text = "";
    }

    private void MidiChangeColorIndexRangeStartLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToInt32(this.midiChangeColorIndexRangeStart.Text.Trim());
      } catch (Exception) {
        this.midiChangeColorIndexRangeStart.Text = "";
        return;
      }
    }

    private void MidiContinuousKnobIndexLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToInt32(this.midiContinuousKnobIndex.Text.Trim());
      } catch (Exception) {
        this.midiContinuousKnobIndex.Text = "";
      }
    }

    private void MidiContinuousKnobPropertyNameLostFocus(object sender, RoutedEventArgs e) {
      var configPropertyName = this.midiContinuousKnobPropertyName.Text;
      if (typeof(Configuration).GetProperty(configPropertyName) == null) {
        this.midiContinuousKnobPropertyName.Text = "";
      }
    }

    private void MidiContinuousKnobStartValueLostFocus(object sender, RoutedEventArgs e) {
      double startNumber;
      try {
        startNumber = Convert.ToDouble(this.midiContinuousKnobStartValue.Text.Trim());
      } catch (Exception) {
        this.midiContinuousKnobStartValue.Text = "";
        return;
      }
      try {
        double endNumber = Convert.ToDouble(this.midiContinuousKnobEndValue.Text.Trim());
        if (endNumber < startNumber) {
          this.midiContinuousKnobEndValue.Text = "";
          this.midiContinuousKnobEndValue.Focus();
        }
      } catch (Exception) {
        this.midiContinuousKnobEndValue.Text = "";
        this.midiContinuousKnobEndValue.Focus();
      }
    }

    private void MidiContinuousKnobEndValueLostFocus(object sender, RoutedEventArgs e) {
      double endNumber;
      try {
        endNumber = Convert.ToDouble(this.midiContinuousKnobEndValue.Text.Trim());
      } catch (Exception) {
        this.midiContinuousKnobEndValue.Text = "";
        return;
      }
      try {
        double startNumber = Convert.ToDouble(this.midiContinuousKnobStartValue.Text.Trim());
        if (endNumber < startNumber) {
          this.midiContinuousKnobStartValue.Text = "";
          this.midiContinuousKnobStartValue.Focus();
        }
      } catch (Exception) {
        this.midiContinuousKnobStartValue.Text = "";
        this.midiContinuousKnobStartValue.Focus();
      }
    }

    private void MidiDiscreteKnobIndexLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToInt32(this.midiDiscreteKnobIndex.Text.Trim());
      } catch (Exception) {
        this.midiDiscreteKnobIndex.Text = "";
      }
    }

    private void MidiDiscreteKnobPropertyNameLostFocus(object sender, RoutedEventArgs e) {
      var configPropertyName = this.midiDiscreteKnobPropertyName.Text;
      if (typeof(Configuration).GetProperty(configPropertyName) == null) {
        this.midiDiscreteKnobPropertyName.Text = "";
      }
    }

    private void MidiDiscreteKnobNumPossibleValuesLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToInt32(this.midiDiscreteKnobNumPossibleValues.Text.Trim());
      } catch (Exception) {
        this.midiDiscreteKnobNumPossibleValues.Text = "";
      }
    }

    private void MidiLogarithmicKnobIndexLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToInt32(this.midiLogarithmicKnobIndex.Text.Trim());
      } catch (Exception) {
        this.midiLogarithmicKnobIndex.Text = "";
      }
    }

    private void MidiLogarithmicKnobPropertyNameLostFocus(object sender, RoutedEventArgs e) {
      var configPropertyName = this.midiLogarithmicKnobPropertyName.Text;
      if (typeof(Configuration).GetProperty(configPropertyName) == null) {
        this.midiLogarithmicKnobPropertyName.Text = "";
      }
    }

    private void MidiLogarithmicKnobNumPossibleValuesLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToInt32(this.midiLogarithmicKnobNumPossibleValues.Text.Trim());
      } catch (Exception) {
        this.midiLogarithmicKnobNumPossibleValues.Text = "";
      }
    }

    private void MidiLogarithmicKnobStartValueLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToDouble(this.midiLogarithmicKnobStartValue.Text.Trim());
      } catch (Exception) {
        this.midiLogarithmicKnobStartValue.Text = "";
      }
    }

    private void MidiAdsrLevelDriverIndexRangeStartLostFocus(object sender, RoutedEventArgs e) {
      try {
        Convert.ToInt32(this.midiAdsrLevelDriverIndexRangeStart.Text.Trim());
      } catch (Exception) {
        this.midiAdsrLevelDriverIndexRangeStart.Text = "";
        return;
      }
    }

    private void OpenMidiHUD(object sender, RoutedEventArgs e) {
      this.midiHUDWindow = new MidiHUDWindow(this.config);
      this.midiHUDWindow.Closed += MidiHUDClosed;
      this.midiHUDWindow.Show();
    }

    private void CloseMidiHUD(object sender, RoutedEventArgs e) {
      this.midiHUDWindow.Close();
      this.midiHUDWindow = null;
    }

    private void MidiHUDClosed(object sender, EventArgs e) {
      this.config.midiHUDEnabled = false;
    }

    private void RefreshDomePorts(object sender, RoutedEventArgs e) {
      this.domeEnabled.IsChecked = false;

      this.domeTeensy1.Items.Clear();
      this.domeTeensy2.Items.Clear();
      this.domeTeensy3.Items.Clear();
      this.domeTeensy4.Items.Clear();
      this.domeTeensy5.Items.Clear();
      foreach (string portName in System.IO.Ports.SerialPort.GetPortNames()) {
        this.domeTeensy1.Items.Add(portName);
        this.domeTeensy2.Items.Add(portName);
        this.domeTeensy3.Items.Add(portName);
        this.domeTeensy4.Items.Add(portName);
        this.domeTeensy5.Items.Add(portName);
      }

      this.domeTeensy1.SelectedValue = this.config.domeTeensyUSBPort1;
      this.domeTeensy2.SelectedValue = this.config.domeTeensyUSBPort2;
      this.domeTeensy3.SelectedValue = this.config.domeTeensyUSBPort3;
      this.domeTeensy4.SelectedValue = this.config.domeTeensyUSBPort4;
      this.domeTeensy5.SelectedValue = this.config.domeTeensyUSBPort5;
    }

    private void DomePortChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.domeTeensy1.SelectedIndex != -1) {
        this.config.domeTeensyUSBPort1 = this.domeTeensy1.SelectedItem as string;
      }
      if (this.domeTeensy2.SelectedIndex != -1) {
        this.config.domeTeensyUSBPort2 = this.domeTeensy2.SelectedItem as string;
      }
      if (this.domeTeensy3.SelectedIndex != -1) {
        this.config.domeTeensyUSBPort3 = this.domeTeensy3.SelectedItem as string;
      }
      if (this.domeTeensy4.SelectedIndex != -1) {
        this.config.domeTeensyUSBPort4 = this.domeTeensy4.SelectedItem as string;
      }
      if (this.domeTeensy5.SelectedIndex != -1) {
        this.config.domeTeensyUSBPort5 = this.domeTeensy5.SelectedItem as string;
      }
      this.op.Reboot();
    }

    private void OpenDomeSimulator(object sender, RoutedEventArgs e) {
      this.domeSimulatorWindow = new DomeSimulatorWindow(this.config);
      this.domeSimulatorWindow.Closed += DomeSimulatorClosed;
      this.domeSimulatorWindow.Show();
    }

    private void CloseDomeSimulator(object sender, RoutedEventArgs e) {
      this.domeSimulatorWindow.Close();
      this.domeSimulatorWindow = null;
    }

    private void DomeSimulatorClosed(object sender, EventArgs e) {
      this.config.domeSimulationEnabled = false;
    }

    private void OpenBarSimulator(object sender, RoutedEventArgs e) {
      this.barSimulatorWindow = new BarSimulatorWindow(this.config);
      this.barSimulatorWindow.Closed += BarSimulatorClosed;
      this.barSimulatorWindow.Show();
    }

    private void CloseBarSimulator(object sender, RoutedEventArgs e) {
      this.barSimulatorWindow.Close();
      this.barSimulatorWindow = null;
    }

    private void BarSimulatorClosed(object sender, EventArgs e) {
      this.config.barSimulationEnabled = false;
    }

    private void RefreshBarUSBPorts(object sender, RoutedEventArgs e) {
      this.barEnabled.IsChecked = false;

      this.barUSBPorts.Items.Clear();
      foreach (string portName in System.IO.Ports.SerialPort.GetPortNames()) {
        this.barUSBPorts.Items.Add(portName);
      }

      this.barUSBPorts.SelectedValue = this.config.barTeensyUSBPort;
    }

    private void BarUSBPortsChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.barUSBPorts.SelectedIndex == -1) {
        return;
      }
      this.config.barTeensyUSBPort = this.barUSBPorts.SelectedItem as string;
      this.op.Reboot();
    }

    private void OpenStageSimulator(object sender, RoutedEventArgs e) {
      this.stageSimulatorWindow = new StageSimulatorWindow(this.config);
      this.stageSimulatorWindow.Closed += StageSimulatorClosed;
      this.stageSimulatorWindow.Show();
    }

    private void CloseStageSimulator(object sender, RoutedEventArgs e) {
      this.stageSimulatorWindow.Close();
      this.stageSimulatorWindow = null;
    }

    private void StageSimulatorClosed(object sender, EventArgs e) {
      this.config.stageSimulationEnabled = false;
    }

    private void RefreshStageUSBPorts(object sender, RoutedEventArgs e) {
      this.stageEnabled.IsChecked = false;

      this.stageTeensyUSBPorts1.Items.Clear();
      this.stageTeensyUSBPorts2.Items.Clear();
      foreach (string portName in System.IO.Ports.SerialPort.GetPortNames()) {
        this.stageTeensyUSBPorts1.Items.Add(portName);
        this.stageTeensyUSBPorts2.Items.Add(portName);
      }

      this.stageTeensyUSBPorts1.SelectedValue = this.config.stageTeensyUSBPort1;
      this.stageTeensyUSBPorts2.SelectedValue = this.config.stageTeensyUSBPort1;
    }

    private void StageUSBPortsChanged(
      object sender,
      SelectionChangedEventArgs e
    ) {
      if (this.stageTeensyUSBPorts1.SelectedIndex != -1) {
        this.config.stageTeensyUSBPort1 = this.stageTeensyUSBPorts1.SelectedItem as string;
      }
      if (this.stageTeensyUSBPorts2.SelectedIndex != -1) {
        this.config.stageTeensyUSBPort2 = this.stageTeensyUSBPorts2.SelectedItem as string;
      }
      this.op.Reboot();
    }

  }

}
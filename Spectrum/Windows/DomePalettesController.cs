using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Spectrum.Base;

namespace Spectrum {

  // Drives the named-palette panel in the VJ HUD: a list of saved palette
  // presets (each showing its eight-slot gradient strip) plus a name box and
  // Apply / Save / Delete buttons, bound to config.domePalettes through the
  // shared PaletteService. The direct analogue of DomeScenesController — applying
  // a preset re-fires the live palette's Item[] notification, so the pickers and
  // the dome both refresh automatically; this controller only manages the preset
  // list.
  //
  // Every mutation runs on the UI thread (native GUI writes always do), so it
  // calls PaletteService directly rather than through the web ControlGateway.
  public class DomePalettesController {
    private readonly Configuration config;
    private readonly PaletteService service;
    private readonly Dispatcher dispatcher;
    private readonly ListBox presetList;
    private readonly TextBox nameBox;

    // The stored DomePalette snapshots themselves (not just names), so the list's
    // item template can render each preset's gradient strip.
    public ObservableCollection<DomePalette> Presets { get; } =
      new ObservableCollection<DomePalette>();

    // True while we repopulate Presets, so the list's SelectionChanged echo
    // doesn't clobber the name box mid-refresh.
    private bool refreshing;

    public DomePalettesController(
      Configuration config, ListBox presetList, TextBox nameBox,
      ButtonBase saveButton, ButtonBase applyButton, ButtonBase deleteButton
    ) {
      this.config = config;
      this.service = new PaletteService(config);
      this.dispatcher = presetList.Dispatcher;
      this.presetList = presetList;
      this.nameBox = nameBox;
      presetList.ItemsSource = this.Presets;
      presetList.SelectionChanged += this.OnSelectionChanged;
      saveButton.Click += (s, e) => this.Save();
      applyButton.Click += (s, e) => this.Apply();
      deleteButton.Click += (s, e) => this.Delete();
      this.config.PropertyChanged += this.OnConfigChanged;
      this.Refresh();
    }

    private void OnConfigChanged(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName != nameof(this.config.domePalettes)) {
        return;
      }
      // A mutation may land off the UI thread; marshal the refresh onto the
      // Dispatcher (mirrors DomeScenesController).
      this.dispatcher.BeginInvoke(new Action(this.Refresh));
    }

    // Selecting a saved preset fills the name box so Save-over-that-preset is one
    // click and Apply/Delete have an obvious target.
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (this.refreshing) {
        return;
      }
      if (this.presetList.SelectedItem is DomePalette palette) {
        this.nameBox.Text = palette.Name;
      }
    }

    private void Refresh() {
      this.refreshing = true;
      string selected = (this.presetList.SelectedItem as DomePalette)?.Name;
      this.Presets.Clear();
      if (this.config.domePalettes != null) {
        foreach (DomePalette palette in this.config.domePalettes) {
          if (palette != null && palette.Name != null) {
            this.Presets.Add(palette);
          }
        }
      }
      // Preserve the selection across the rebuild when it still exists.
      if (selected != null) {
        foreach (DomePalette palette in this.Presets) {
          if (string.Equals(
                palette.Name, selected, StringComparison.OrdinalIgnoreCase)) {
            this.presetList.SelectedItem = palette;
            break;
          }
        }
      }
      this.refreshing = false;
    }

    private void Save() {
      string name = this.nameBox.Text?.Trim();
      if (string.IsNullOrEmpty(name)) {
        MessageBox.Show("Type a palette name to save.", "Palettes",
          MessageBoxButton.OK, MessageBoxImage.Information);
        return;
      }
      if (this.NameExists(name) && MessageBox.Show(
            "Overwrite palette \"" + name + "\"?", "Palettes",
            MessageBoxButton.OKCancel, MessageBoxImage.Question)
          != MessageBoxResult.OK) {
        return;
      }
      this.Report(this.service.Save(name));
    }

    private void Apply() {
      string name = this.PaletteTarget();
      if (name == null) {
        return;
      }
      this.Report(this.service.Apply(name));
    }

    private void Delete() {
      string name = this.PaletteTarget();
      if (name == null) {
        return;
      }
      if (MessageBox.Show("Delete palette \"" + name + "\"?", "Palettes",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning)
          != MessageBoxResult.OK) {
        return;
      }
      this.Report(this.service.Delete(name));
    }

    // The preset Apply/Delete act on: the list selection, else the name box.
    private string PaletteTarget() {
      string name = (this.presetList.SelectedItem as DomePalette)?.Name;
      if (string.IsNullOrEmpty(name)) {
        name = this.nameBox.Text?.Trim();
      }
      if (string.IsNullOrEmpty(name)) {
        MessageBox.Show("Pick a palette first.", "Palettes",
          MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
      }
      return name;
    }

    private bool NameExists(string name) {
      foreach (DomePalette palette in this.Presets) {
        if (string.Equals(
              palette.Name, name, StringComparison.OrdinalIgnoreCase)) {
          return true;
        }
      }
      return false;
    }

    private void Report((bool ok, string error) result) {
      if (!result.ok) {
        MessageBox.Show(result.error, "Palettes",
          MessageBoxButton.OK, MessageBoxImage.Warning);
      }
    }
  }

}

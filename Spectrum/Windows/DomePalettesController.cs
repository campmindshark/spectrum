using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Spectrum.Base;

namespace Spectrum {

  // Drives the native list of named live palettes. Selecting an entry chooses
  // which colors the editor changes; there is no independent bank and no Apply.
  public class DomePalettesController {
    private readonly Configuration config;
    private readonly PaletteService service;
    private readonly Dispatcher dispatcher;
    private readonly ListBox paletteList;
    private readonly TextBox nameBox;
    private readonly Action<DomePalette> selectionChanged;

    public ObservableCollection<DomePalette> Palettes { get; } =
      new ObservableCollection<DomePalette>();

    private bool refreshing;

    public DomePalettesController(
      Configuration config, ListBox paletteList, TextBox nameBox,
      ButtonBase addButton, ButtonBase renameButton, ButtonBase deleteButton,
      Action<DomePalette> selectionChanged
    ) {
      this.config = config;
      this.service = new PaletteService(config);
      this.dispatcher = paletteList.Dispatcher;
      this.paletteList = paletteList;
      this.nameBox = nameBox;
      this.selectionChanged = selectionChanged;
      paletteList.ItemsSource = this.Palettes;
      paletteList.SelectionChanged += this.OnSelectionChanged;
      addButton.Click += (s, e) => this.Add();
      renameButton.Click += (s, e) => this.Rename();
      deleteButton.Click += (s, e) => this.Delete();
      this.config.PropertyChanged += this.OnConfigChanged;
      this.Refresh();
    }

    private DomePalette Selected =>
      this.paletteList.SelectedItem as DomePalette;

    private void OnConfigChanged(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == nameof(this.config.domePalettes)) {
        this.dispatcher.BeginInvoke(new Action(this.Refresh));
      }
    }

    private void OnPaletteEdited(object sender, PropertyChangedEventArgs e) {
      if (this.refreshing || e.PropertyName != "Item[]" ||
          sender is not DomePalette palette) {
        return;
      }
      this.service.ReplaceColors(palette.Name, palette.Colors);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (this.refreshing) {
        return;
      }
      DomePalette selected = this.Selected;
      if (selected != null) {
        this.nameBox.Text = selected.Name;
      }
      this.selectionChanged?.Invoke(selected);
    }

    private void Refresh() {
      string selectedName = this.Selected?.Name;
      this.refreshing = true;
      this.Palettes.Clear();
      if (!this.config.domePalettes.IsDefaultOrEmpty) {
        foreach (DomePaletteSnapshot snapshot in this.config.domePalettes) {
          DomePalette palette = snapshot?.ToPalette();
          if (palette != null && !string.IsNullOrWhiteSpace(palette.Name)) {
            palette.PropertyChanged += this.OnPaletteEdited;
            this.Palettes.Add(palette);
          }
        }
      }
      DomePalette selected = Find(this.Palettes, selectedName) ??
        (this.Palettes.Count > 0 ? this.Palettes[0] : null);
      this.paletteList.SelectedItem = selected;
      if (selected != null) {
        this.nameBox.Text = selected.Name;
      }
      this.refreshing = false;
      this.selectionChanged?.Invoke(selected);
    }

    // Add duplicates the currently selected colors under the typed name. This
    // makes variations quick while the first palette can still be created empty.
    private void Add() {
      string name = this.nameBox.Text?.Trim();
      LEDColor[] colors = this.Selected?.Colors;
      (bool ok, string error) = this.service.Add(name, colors);
      if (!this.Report((ok, error))) {
        return;
      }
      this.Refresh();
      DomePalette added = Find(this.Palettes, name);
      if (added != null) {
        this.paletteList.SelectedItem = added;
      }
    }

    private void Rename() {
      DomePalette selected = this.Selected;
      if (selected == null) {
        this.PickFirst();
        return;
      }
      string newName = this.nameBox.Text?.Trim();
      (bool ok, string error) = this.service.Rename(selected.Name, newName);
      if (!this.Report((ok, error))) {
        return;
      }
      this.Refresh();
      DomePalette renamed = Find(this.Palettes, newName);
      if (renamed != null) {
        this.paletteList.SelectedItem = renamed;
      }
    }

    private void Delete() {
      DomePalette selected = this.Selected;
      if (selected == null) {
        this.PickFirst();
        return;
      }
      if (MessageBox.Show(
            "Delete palette \"" + selected.Name + "\"? Layers using it " +
            "will switch to the first palette.",
            "Palettes", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
          != MessageBoxResult.OK) {
        return;
      }
      if (this.Report(this.service.Delete(selected.Name))) {
        this.Refresh();
      }
    }

    private void PickFirst() {
      MessageBox.Show("Pick a palette first.", "Palettes",
        MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool Report((bool ok, string error) result) {
      if (result.ok) {
        return true;
      }
      MessageBox.Show(result.error, "Palettes",
        MessageBoxButton.OK, MessageBoxImage.Warning);
      return false;
    }

    private static DomePalette Find(
      ObservableCollection<DomePalette> palettes, string name
    ) {
      if (name == null) {
        return null;
      }
      foreach (DomePalette palette in palettes) {
        if (string.Equals(
              palette.Name, name, StringComparison.OrdinalIgnoreCase)) {
          return palette;
        }
      }
      return null;
    }
  }

}

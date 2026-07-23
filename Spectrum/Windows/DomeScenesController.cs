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

  // Drives a dome scenes bar: a combo of saved scene names plus a name box and
  // Save / Load / Delete buttons, bound to config.domeScenes through the shared
  // SceneService. Analogous to DomeLayersController and shared by MainWindow and
  // the VJ HUD. Applying a scene re-fires domeLayerStack (and the two global
  // speeds), so the existing DomeLayersController.OnConfigChanged rebuilds the
  // layer rows automatically — this controller only manages the scene list.
  //
  // Every mutation runs on the UI thread (native GUI writes always do), so it
  // calls SceneService directly on the application-state dispatcher thread.
  public class DomeScenesController {
    private readonly Configuration config;
    private readonly SceneService service;
    private readonly Dispatcher dispatcher;
    private readonly ComboBox sceneCombo;
    private readonly TextBox nameBox;

    public ObservableCollection<string> SceneNames { get; } =
      new ObservableCollection<string>();

    // True while we repopulate SceneNames, so the combo's SelectionChanged echo
    // doesn't clobber the name box mid-refresh.
    private bool refreshing;

    public DomeScenesController(
      Configuration config, ComboBox sceneCombo, TextBox nameBox,
      ButtonBase saveButton, ButtonBase loadButton, ButtonBase deleteButton
    ) {
      this.config = config;
      this.service = new SceneService(config, DomeLayerCatalog.Metadata);
      this.dispatcher = sceneCombo.Dispatcher;
      this.sceneCombo = sceneCombo;
      this.nameBox = nameBox;
      sceneCombo.ItemsSource = this.SceneNames;
      sceneCombo.SelectionChanged += this.OnSelectionChanged;
      saveButton.Click += (s, e) => this.Save();
      loadButton.Click += (s, e) => this.Load();
      deleteButton.Click += (s, e) => this.Delete();
      this.config.PropertyChanged += this.OnConfigChanged;
      this.Refresh();
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e) {
      if (e.PropertyName != nameof(this.config.domeScenes)) {
        return;
      }
      // A web save/delete may land off the UI thread (the gateway posts to the
      // Dispatcher, but be defensive); marshal the refresh onto the Dispatcher.
      this.dispatcher.BeginInvoke(new Action(this.Refresh));
    }

    // Selecting a saved scene fills the name box so Save-over-that-scene is one
    // click and Load/Delete have an obvious target.
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (this.refreshing) {
        return;
      }
      if (this.sceneCombo.SelectedItem is string name) {
        this.nameBox.Text = name;
      }
    }

    private void Refresh() {
      this.refreshing = true;
      string? selected = this.sceneCombo.SelectedItem as string;
      this.SceneNames.Clear();
      foreach (string name in this.service.Names()) {
        this.SceneNames.Add(name);
      }
      // Preserve the selection across the rebuild when it still exists.
      if (selected != null && this.SceneNames.Contains(selected)) {
        this.sceneCombo.SelectedItem = selected;
      }
      this.refreshing = false;
    }

    private void Save() {
      string? name = this.nameBox.Text?.Trim();
      if (string.IsNullOrEmpty(name)) {
        MessageBox.Show("Type a scene name to save.", "Scenes",
          MessageBoxButton.OK, MessageBoxImage.Information);
        return;
      }
      if (this.NameExists(name) && MessageBox.Show(
            "Overwrite scene \"" + name + "\"?", "Scenes",
            MessageBoxButton.OKCancel, MessageBoxImage.Question)
          != MessageBoxResult.OK) {
        return;
      }
      this.Report(this.service.Save(name));
    }

    private void Load() {
      string? name = this.SceneTarget();
      if (name == null) {
        return;
      }
      this.Report(this.service.Apply(name));
    }

    private void Delete() {
      string? name = this.SceneTarget();
      if (name == null) {
        return;
      }
      if (MessageBox.Show("Delete scene \"" + name + "\"?", "Scenes",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning)
          != MessageBoxResult.OK) {
        return;
      }
      this.Report(this.service.Delete(name));
    }

    // The scene Load/Delete act on: the combo selection, else the name box.
    private string? SceneTarget() {
      string? name = this.sceneCombo.SelectedItem as string;
      if (string.IsNullOrEmpty(name)) {
        name = this.nameBox.Text?.Trim();
      }
      if (string.IsNullOrEmpty(name)) {
        MessageBox.Show("Pick a scene first.", "Scenes",
          MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
      }
      return name;
    }

    private bool NameExists(string name) {
      foreach (string existing in this.SceneNames) {
        if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)) {
          return true;
        }
      }
      return false;
    }

    private void Report((bool ok, string? error) result) {
      if (!result.ok) {
        MessageBox.Show(result.error, "Scenes",
          MessageBoxButton.OK, MessageBoxImage.Warning);
      }
    }
  }

}

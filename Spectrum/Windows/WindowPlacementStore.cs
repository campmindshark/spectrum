using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Spectrum {

  // Persists the main window's normal bounds independently from application
  // composition and validates them against the current virtual desktop before
  // applying a saved placement.
  internal static class WindowPlacementStore {
    private sealed class State {
      public double Left { get; set; }
      public double Top { get; set; }
      public double Width { get; set; }
      public double Height { get; set; }
      public bool Maximized { get; set; }
    }

    internal static void Restore(Window window, string path) {
      try {
        if (!File.Exists(path)) {
          return;
        }
        State? state = JsonSerializer.Deserialize<State>(
          File.ReadAllText(path));
        if (state == null ||
            state.Width < window.MinWidth ||
            state.Height < window.MinHeight) {
          return;
        }
        var saved = new Rect(
          state.Left, state.Top, state.Width, state.Height);
        var desktop = new Rect(
          SystemParameters.VirtualScreenLeft,
          SystemParameters.VirtualScreenTop,
          SystemParameters.VirtualScreenWidth,
          SystemParameters.VirtualScreenHeight);
        if (!saved.IntersectsWith(desktop)) {
          return;
        }
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = state.Left;
        window.Top = state.Top;
        window.Width = state.Width;
        window.Height = state.Height;
        if (state.Maximized) {
          window.WindowState = WindowState.Maximized;
        }
      } catch (Exception error) {
        Debug.WriteLine(
          "Could not restore window placement: " + error.Message);
      }
    }

    internal static void Save(Window window, string path) {
      try {
        Rect bounds = window.WindowState == WindowState.Normal
          ? new Rect(
              window.Left, window.Top, window.Width, window.Height)
          : window.RestoreBounds;
        var state = new State {
          Left = bounds.Left,
          Top = bounds.Top,
          Width = bounds.Width,
          Height = bounds.Height,
          Maximized = window.WindowState == WindowState.Maximized,
        };
        File.WriteAllText(
          path,
          JsonSerializer.Serialize(
            state,
            new JsonSerializerOptions { WriteIndented = true }));
      } catch (Exception error) {
        Debug.WriteLine(
          "Could not save window placement: " + error.Message);
      }
    }
  }
}

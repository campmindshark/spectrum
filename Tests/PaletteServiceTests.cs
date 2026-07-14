using System;
using System.Collections.Generic;
using System.ComponentModel;
using Spectrum.Base;

namespace Spectrum.LayerPipeline.Tests {

  internal static class PaletteServiceTests {

    public static void Register(Action<string, Action> run) {
      run("palette presets are bank-local value snapshots", PresetSnapshots);
      run("scene recall restores its captured palette", ScenePaletteRecall);
    }

    private static void PresetSnapshots() {
      var config = new global::Spectrum.SpectrumConfiguration();
      int bank = 2;
      int first = bank * PaletteService.LiveSlots;
      config.colorPalette.SetColor(0, 0x010203);
      config.colorPalette.SetGradientColor(first, 0x112233, 0x445566);
      config.colorPalette.SetColor(first + 1, 0x778899);
      LEDColor originalLiveColor = config.colorPalette.colors[first];

      var palettes = new PaletteService(config);
      (bool saved, string saveError) = palettes.Save("  Aurora  ", bank);
      Assert(saved, saveError);
      Assert(config.domePalettes.Count == 1 &&
        config.domePalettes[0].Name == "Aurora",
        "palette name was not normalized");
      DomePalette stored = config.domePalettes[0];
      Assert(!ReferenceEquals(originalLiveColor, stored.Colors[0]),
        "saved palette aliases the live color");

      originalLiveColor.color1 = 0xFFFFFF;
      originalLiveColor.color2 = 0;
      config.colorPalette.SetColor(first + 1, 0x000000);
      config.colorPalette.SetColor(first + 2, 0xABCDEF);
      AssertColor(stored.Colors[0], 0x112233, 0x445566,
        "saved gradient changed with the live palette");

      int notifications = 0;
      string notificationName = null;
      config.colorPalette.PropertyChanged +=
        (object sender, PropertyChangedEventArgs e) => {
          notifications++;
          notificationName = e.PropertyName;
        };
      (bool applied, string applyError) = palettes.Apply("aurora", bank);
      Assert(applied, applyError);

      AssertColor(config.colorPalette.colors[first], 0x112233, 0x445566,
        "stored gradient was not restored");
      AssertColor(config.colorPalette.colors[first + 1], 0x778899, null,
        "stored solid color was not restored");
      Assert(config.colorPalette.colors[first + 2] == null,
        "stored empty slot did not clear the live slot");
      Assert(config.colorPalette.GetSingleColor(0) == 0x010203,
        "recalling bank 2 changed bank 0");
      Assert(notifications == 1 && notificationName == "Item[]",
        "palette recall was not published as one atomic change");
      Assert(!ReferenceEquals(
          config.colorPalette.colors[first], stored.Colors[0]),
        "recalled palette aliases its stored preset");

      config.colorPalette.colors[first].color1 = 0;
      Assert(stored.Colors[0].Color1 == 0x112233,
        "editing a recalled color mutated its preset");
    }

    private static void ScenePaletteRecall() {
      var config = new global::Spectrum.SpectrumConfiguration {
        domeLayerStack = new List<DomeLayerSettings>(),
      };
      config.colorPalette.SetGradientColor(0, 0x102030, 0xA0B0C0);

      var scenes = new SceneService(config);
      (bool saved, string saveError) = scenes.Save("Blue hour");
      Assert(saved, saveError);
      DomeScene stored = config.domeScenes[0];
      LEDColor storedColor = stored.Palette[0];
      Assert(!ReferenceEquals(storedColor, config.colorPalette.colors[0]),
        "scene palette aliases the live palette");

      config.colorPalette.SetColor(0, 0xFFFFFF);
      (bool applied, string applyError) = scenes.Apply("blue HOUR");
      Assert(applied, applyError);
      AssertColor(config.colorPalette.colors[0], 0x102030, 0xA0B0C0,
        "scene did not restore its captured palette");
      Assert(!ReferenceEquals(storedColor, config.colorPalette.colors[0]),
        "recalled scene aliases its stored palette");

      config.domeScenes = new List<DomeScene> {
        new DomeScene {
          Name = "Legacy",
          Layers = new List<DomeLayerSettings>(),
          Palette = null,
        },
      };
      config.colorPalette.SetColor(0, 0x13579B);
      (bool legacyApplied, string legacyError) = scenes.Apply("Legacy");
      Assert(legacyApplied, legacyError);
      Assert(config.colorPalette.GetSingleColor(0) == 0x13579B,
        "legacy scene without a palette erased live colors");
    }

    private static void AssertColor(
      LEDColor actual, int color1, int? color2, string message
    ) {
      Assert(actual != null, message + ": color is null");
      Assert(actual.Color1 == color1, message + ": first endpoint changed");
      Assert(actual.IsGradient == color2.HasValue,
        message + ": gradient state changed");
      if (color2.HasValue) {
        Assert(actual.Color2 == color2.Value,
          message + ": second endpoint changed");
      }
    }

    private static void Assert(bool condition, string message) {
      if (!condition) {
        throw new InvalidOperationException(message);
      }
    }
  }
}

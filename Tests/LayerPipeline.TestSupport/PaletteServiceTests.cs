using System;
using System.Collections.Generic;
using Spectrum.Base;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class PaletteServiceTests {

    public static void Register(Action<string, Action> run) {
      run("named live palettes preserve layer references", PaletteMutations);
      run("scene recall does not overwrite live palettes", ScenePaletteOwnership);
    }

    private static void PaletteMutations() {
      var config = DirectConfig(
        Palette("Warm", 0xAA1100),
        Palette("Cool", 0x0011AA),
        Palette("Mono", 0x777777));
      config.ReplaceDomeLayerStack(new List<DomeLayerSettings> {
        LayerWithPalette("warm", 0),
        LayerWithPalette("cool", 1),
        LayerWithPalette("mono", 2),
      });
      config.ReplaceDomeScenes(new List<DomeScene> {
        new DomeScene {
          Name = "Look",
          Layers = new List<DomeLayerSettings> {
            LayerWithPalette("saved-cool", 1),
            LayerWithPalette("saved-mono", 2),
          },
        },
      });
      var service = new PaletteService(config);

      (bool added, string? addError) = service.Add(
        "Warm variation", config.domePalettes[0].ToPalette().Colors);
      Assert(added, addError);
      Assert(config.domePalettes.Length == 4 &&
          config.domePalettes[3].Name == "Warm variation",
        "palette copy was not appended");
      Assert(config.domePalettes[0].Colors[0] ==
          config.domePalettes[3].Colors[0],
        "palette copy changed its source colors");

      (bool renamed, string? renameError) = service.Rename("Cool", "Ocean");
      Assert(renamed, renameError);
      Assert(config.domePalettes[1].Name == "Ocean",
        "palette was not renamed in place");

      (bool deleted, string? deleteError) = service.Delete("Ocean");
      Assert(deleted, deleteError);
      Assert(config.domePalettes.Length == 3 &&
          config.domePalettes[1].Name == "Mono",
        "deleted palette remained in the ordered list");
      Assert(config.domeLayerStack[0].RendererParams["palette"] == 0 &&
          config.domeLayerStack[1].RendererParams["palette"] == 0 &&
          config.domeLayerStack[2].RendererParams["palette"] == 1,
        "live layer palette references were not remapped on delete");
      Assert(config.domeScenes[0].Layers[0].RendererParams["palette"] == 0 &&
          config.domeScenes[0].Layers[1].RendererParams["palette"] == 1,
        "saved scene palette references were not remapped on delete");
    }

    private static void ScenePaletteOwnership() {
      var config = DirectConfig(Palette("Blue hour", 0x102030));
      config.ReplaceDomeLayerStack(new List<DomeLayerSettings>());
      var scenes = new SceneService(config, DomeLayerCatalog.Metadata);
      (bool saved, string? saveError) = scenes.Save("Look");
      Assert(saved, saveError);

      (bool replaced, string? replaceError) = new PaletteService(config)
        .ReplaceColors("Blue hour", new[] { new LEDColor(0x13579B) });
      Assert(replaced, replaceError);
      (bool applied, string? applyError) = scenes.Apply("Look");
      Assert(applied, applyError);
      Assert(config.domePalettes[0].GetSingleColor(0) == 0x13579B,
        "scene recall overwrote a named live palette");
    }

    private static global::Spectrum.SpectrumConfiguration DirectConfig(
      params DomePalette[] palettes
    ) {
      var config = new global::Spectrum.SpectrumConfiguration();
      config.ReplaceDomePalettes(new List<DomePalette>(palettes));
      return config;
    }

    private static DomePalette Palette(string name, int firstColor) {
      var colors = new LEDColor[DomePalette.SlotCount];
      colors[0] = new LEDColor(firstColor);
      return new DomePalette { Name = name, Colors = colors };
    }

    private static DomeLayerSettings LayerWithPalette(string id, int palette) =>
      new DomeLayerSettings {
        InstanceId = id,
        VisualizerKey = "radial",
        BlendMode = DomeBlend.Default.Id,
        Opacity = 1,
        Enabled = true,
        RendererParams = new Dictionary<string, double> {
          ["palette"] = palette,
        },
      };

    private static void AssertColor(
      LEDColor? actual, int color1, int? color2, string message
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

  }
}

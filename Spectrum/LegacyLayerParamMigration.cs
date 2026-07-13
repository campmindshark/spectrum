using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Spectrum.Base;

namespace Spectrum {

  // Forward migration for configs that predate the layer stack and per-layer
  // params. Two retired kinds of Configuration state are involved:
  //  - the domeActiveVis single-visualizer selector, which seeds a synthesized
  //    stack when the file has no usable domeLayerStack;
  //  - the global visualizer-tuning properties (domeRadialSize,
  //    domeVolumeRotationSpeed, ...), which seed each layer's Params bag,
  //    keyed by DomeLayerParam.LegacySetting.
  // Because the properties no longer exist, the deserializer silently drops
  // their XML elements — so this reads the raw config file instead. The stale
  // elements then disappear on the next save, making the migration
  // self-retiring.
  //
  // Runs from MainWindow.LoadConfig before the Operator exists, so mutating the
  // layer objects in place is safe — nothing has been published to the operator
  // thread yet.
  static class LegacyLayerParamMigration {

    public static void Apply(Configuration config, string configXmlPath) {
      Dictionary<string, double> legacy = configXmlPath != null
        ? ReadNumericElements(configXmlPath)
        : new Dictionary<string, double>();
      EnsureStack(config, legacy);
      SeedLayerParams(config, legacy);
      // Assign stable instance IDs to pre-instance stacks and normalize once at
      // the serializer boundary before the operator can observe them.
      (List<DomeLayerSettings> normalized, string error) =
        StackValidator.Validate(config.domeLayerStack);
      if (error == null) {
        config.domeLayerStack = normalized;
      }
    }

    // If the loaded config has no usable domeLayerStack (missing from an older
    // file, empty, or referencing unknown keys), synthesize a single background
    // layer from the retired domeActiveVis selector — reproducing the
    // pre-layering single-visualizer behavior exactly.
    private static void EnsureStack(
      Configuration config, Dictionary<string, double> legacy
    ) {
      List<DomeLayerSettings> stack = config.domeLayerStack;
      bool valid = stack != null && stack.Count > 0;
      if (valid) {
        foreach (DomeLayerSettings layer in stack) {
          // Any picker-offerable key is valid — not just the legacy eight, or a
          // saved stack containing e.g. a wave/twinkle layer would be wiped
          // here on every load.
          if (
            layer == null || !DomeLayerSettings.IsLayerKey(layer.VisualizerKey)
          ) {
            valid = false;
            break;
          }
        }
      }
      if (valid) {
        return;
      }
      string key = null;
      if (legacy.TryGetValue("domeActiveVis", out double legacyVis)) {
        key = DomeLayerSettings.KeyForLegacyVis((int)legacyVis);
      }
      config.domeLayerStack = new List<DomeLayerSettings>() {
        new DomeLayerSettings() {
          VisualizerKey = key ?? DomeLayerSettings.LegacyVisKeys[0],
          BlendMode = DomeBlend.Over.Name,
          Opacity = 1.0,
          Enabled = true,
        },
      };
    }

    // Seeds each stack layer's Params bag from the retired global tuning
    // properties its schema claims (DomeLayerParam.LegacySetting).
    private static void SeedLayerParams(
      Configuration config, Dictionary<string, double> legacy
    ) {
      List<DomeLayerSettings> stack = config.domeLayerStack;
      if (stack == null || legacy.Count == 0) {
        return;
      }
      foreach (DomeLayerSettings layer in stack) {
        if (layer == null || layer.VisualizerKey == null) {
          continue;
        }
        foreach (
          DomeLayerParam p in DomeLayerSettings.ParamsFor(layer.VisualizerKey)
        ) {
          if (
            p.LegacySetting == null ||
            !legacy.TryGetValue(p.LegacySetting, out double value)
          ) {
            continue;
          }
          // A bag that already has the key postdates the migration (or the
          // user set it); never clobber it.
          if (layer.Params != null && layer.Params.ContainsKey(p.Key)) {
            continue;
          }
          if (layer.Params == null) {
            layer.Params = new Dictionary<string, double>();
          }
          layer.Params[p.Key] = Clamp(p, value);
        }
      }
    }

    // Every root-level element of the config file that parses as a number,
    // keyed by element name. Superset of the legacy settings; Apply filters by
    // descriptor LegacySetting. Any read/parse failure just means no migration.
    private static Dictionary<string, double> ReadNumericElements(string path) {
      var values = new Dictionary<string, double>();
      try {
        XElement root = XDocument.Load(path).Root;
        if (root == null) {
          return values;
        }
        foreach (XElement element in root.Elements()) {
          if (
            !element.HasElements &&
            double.TryParse(
              element.Value, NumberStyles.Float, CultureInfo.InvariantCulture,
              out double value
            )
          ) {
            values[element.Name.LocalName] = value;
          }
        }
      } catch (System.Exception) {
        // Unreadable/malformed config: the deserializer already dealt with it
        // its own way; skipping migration is the right degradation.
      }
      return values;
    }

    // Same coercion the web PUT applies (LayersController.ClampParam): the
    // migrated value must be a value the editors could have produced.
    private static double Clamp(DomeLayerParam p, double v) {
      switch (p.Type) {
        case DomeLayerParamType.Bool:
          return v != 0 ? 1 : 0;
        case DomeLayerParamType.Enum:
          int count = p.Options != null ? p.Options.Length : 0;
          int index = (int)v;
          if (index < 0) {
            index = 0;
          }
          if (count > 0 && index >= count) {
            index = count - 1;
          }
          return index;
        default:
          if (double.IsNaN(v)) {
            return p.Default;
          }
          return v < p.Min ? p.Min : v > p.Max ? p.Max : v;
      }
    }
  }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * The web control for the dome layer stack. Whole-stack last-write-wins (the
   * client always sends its full edited copy), so — unlike the modal dome
   * calibration — it needs no advisory lease: it simply replaces
   * config.domeLayerStack through the ControlGateway, exactly like a native GUI
   * write. The full stack is broadcast on the SSE feed (frame kind "layers") so
   * every client and the native UI converge.
   *
   * The layers array is in stack order: index 0 is the background (bottom),
   * the last entry is the front. blendMode is carried as its enum name.
   */
  public sealed class LayersController {

    // One layer as sent to / received from the client.
    public sealed class LayerDto {
      public string visualizerKey { get; set; }
      public string blendMode { get; set; }
      public double opacity { get; set; }
      public bool enabled { get; set; }
    }

    public sealed class VisualizerOptionDto {
      public string key { get; set; }
      public string label { get; set; }
    }

    // The full snapshot GET returns: the current stack plus the fixed pick-lists
    // (available visualizers and blend modes) the client renders its editors from.
    public sealed class LayersState {
      public IReadOnlyList<LayerDto> layers { get; set; }
      public IReadOnlyList<VisualizerOptionDto> visualizers { get; set; }
      public IReadOnlyList<string> blendModes { get; set; }
    }

    // Guard against an unbounded stack from a buggy/malicious client. Far above
    // any sane number of layers.
    private const int MaxLayers = 16;

    private readonly ControlGateway gateway;
    private readonly Configuration config;
    private static readonly string[] blendModeNames =
      Enum.GetNames(typeof(DomeBlendMode));

    public LayersController(ControlGateway gateway, Configuration config) {
      this.gateway = gateway;
      this.config = config;
    }

    public LayersState State() {
      return new LayersState {
        layers = SerializeStack(this.config),
        visualizers = VisualizerOptions(),
        blendModes = blendModeNames,
      };
    }

    // The current config stack as client DTOs, in stack order (index 0 =
    // background). Shared with ConfigEventStream so the SSE "layers" frame and the
    // GET response are identical in shape.
    public static List<LayerDto> SerializeStack(Configuration config) {
      var list = new List<LayerDto>();
      List<DomeLayerSettings> stack = config.domeLayerStack;
      if (stack != null) {
        foreach (DomeLayerSettings layer in stack) {
          if (layer == null) {
            continue;
          }
          list.Add(new LayerDto {
            visualizerKey = layer.VisualizerKey,
            blendMode = layer.BlendMode.ToString(),
            opacity = layer.Opacity,
            enabled = layer.Enabled,
          });
        }
      }
      return list;
    }

    private static List<VisualizerOptionDto> VisualizerOptions() {
      var options = new List<VisualizerOptionDto>();
      for (int i = 0; i < DomeLayerSettings.LegacyVisKeys.Length; i++) {
        options.Add(new VisualizerOptionDto {
          key = DomeLayerSettings.LegacyVisKeys[i],
          label = DomeLayerSettings.LegacyVisLabels[i],
        });
      }
      return options;
    }

    // Validate the whole incoming stack, then replace config.domeLayerStack via
    // the gateway (snapshot swap on the UI thread). Rejects unknown keys,
    // duplicate visualizers (v1 disallows duplicates — each visualizer is a
    // singleton owning one buffer), out-of-range opacity, unknown blend modes, or
    // an over-long stack, without touching config.
    public async Task<(bool ok, string error)> ReplaceAsync(
      IReadOnlyList<LayerDto> layers
    ) {
      if (layers == null) {
        return (false, "body must be {\"layers\": [...]}");
      }
      if (layers.Count > MaxLayers) {
        return (false, "too many layers (max " + MaxLayers + ")");
      }
      var seen = new HashSet<string>();
      var newStack = new List<DomeLayerSettings>();
      foreach (LayerDto dto in layers) {
        if (dto == null || dto.visualizerKey == null) {
          return (false, "each layer needs a visualizerKey");
        }
        if (Array.IndexOf(
          DomeLayerSettings.LegacyVisKeys, dto.visualizerKey
        ) < 0) {
          return (false, "unknown visualizer key: " + dto.visualizerKey);
        }
        if (!seen.Add(dto.visualizerKey)) {
          return (false, "duplicate visualizer: " + dto.visualizerKey);
        }
        if (!Enum.TryParse(
          dto.blendMode ?? "", out DomeBlendMode mode
        ) || !Enum.IsDefined(typeof(DomeBlendMode), mode)) {
          return (false, "unknown blend mode: " + dto.blendMode);
        }
        double opacity = dto.opacity;
        if (double.IsNaN(opacity) || opacity < 0 || opacity > 1) {
          return (false, "opacity must be between 0 and 1");
        }
        newStack.Add(new DomeLayerSettings {
          VisualizerKey = dto.visualizerKey,
          BlendMode = mode,
          Opacity = opacity,
          Enabled = dto.enabled,
        });
      }
      await this.gateway.InvokeAsync(() => this.config.domeLayerStack = newStack);
      return (true, null);
    }
  }
}

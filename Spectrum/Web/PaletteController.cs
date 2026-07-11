using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Spectrum.Base;

namespace Spectrum.Web {

  /**
   * The web control for the color palette, the counterpart of the native VJ HUD
   * palette panel. Two surfaces, both funneled through the ControlGateway so the
   * reads and writes land on the serialization thread exactly like a native GUI
   * write:
   *
   *  - the *live* palette — colorPalette slots 0-7, the eight gradient pairs
   *    every visualizer consumes. Whole-palette last-write-wins, like the layer
   *    stack: the client PUTs all eight slots, broadcast on the SSE "palette"
   *    frame so every client converges.
   *  - named *presets* — parallel to scenes: save the live palette under a name,
   *    apply/delete by name. The preset list is broadcast on the SSE "palettes"
   *    frame; applying a preset rewrites the live slots, which broadcast over the
   *    "palette" frame.
   *
   * All the logic lives in the shared PaletteService (Base), so this surface and
   * the native DomePalettesController can't diverge. A slot is carried over JSON
   * as { start, end } hex strings ("#rrggbb"); end == null means a single-color
   * (non-gradient) slot, and start == null means an empty slot.
   */
  public sealed class PaletteController {

    // One palette slot over the wire: start color plus an optional gradient end.
    public sealed class SlotDto {
      public string start { get; set; }
      public string end { get; set; }
    }

    // A named preset: its name plus its eight slots (for the client's preview).
    public sealed class PresetDto {
      public string name { get; set; }
      public List<SlotDto> colors { get; set; }
    }

    private readonly ControlGateway gateway;
    private readonly Configuration config;
    private readonly PaletteService service;

    public PaletteController(ControlGateway gateway, Configuration config) {
      this.gateway = gateway;
      this.config = config;
      this.service = new PaletteService(config);
    }

    // GET /api/palette — the live eight-slot palette.
    public object LiveState() {
      return new { colors = BuildLive(this.config) };
    }

    // PUT /api/palette — replace the live palette (slots 0-7) wholesale. Runs the
    // conversion + write on the serialization thread inside the gateway action,
    // so the single Item[] notification (PaletteService.Restore) fans out to
    // every client. Rejects malformed hex before touching config.
    public async Task<(bool ok, string error)> SetLiveAsync(List<SlotDto> colors) {
      if (colors == null) {
        return (false, "body must be {\"colors\": [ ... ]}");
      }
      LEDColor[] slots;
      try {
        slots = ToColors(colors);
      } catch (ArgumentException e) {
        return (false, e.Message);
      }
      await this.gateway.InvokeAsync(
        () => PaletteService.Restore(this.config, slots));
      return (true, null);
    }

    // GET /api/palettes — the saved presets (name + colors), in stored order.
    public object PresetsState() {
      return new { palettes = BuildPresets(this.config) };
    }

    // Save the live palette under `name` (overwriting an existing preset with
    // that name). The read of the live slots and the domePalettes swap both run
    // on the serialization thread inside the gateway action.
    public async Task<(bool ok, string error)> SaveAsync(string name) {
      (bool ok, string error) result = (false, "not run");
      await this.gateway.InvokeAsync(() => result = this.service.Save(name));
      return result;
    }

    public async Task<(bool ok, string error)> ApplyAsync(string name) {
      (bool ok, string error) result = (false, "not run");
      await this.gateway.InvokeAsync(() => result = this.service.Apply(name));
      return result;
    }

    public async Task<(bool ok, string error)> DeleteAsync(string name) {
      (bool ok, string error) result = (false, "not run");
      await this.gateway.InvokeAsync(() => result = this.service.Delete(name));
      return result;
    }

    // The live palette as slot DTOs. Shared by the REST GET and the SSE "palette"
    // frame (ConfigEventStream), so both render from the same server truth.
    public static List<SlotDto> BuildLive(Configuration config) {
      return ToSlots(PaletteService.Snapshot(config));
    }

    // The saved presets as DTOs. Shared by the REST GET and the SSE "palettes"
    // frame.
    public static List<PresetDto> BuildPresets(Configuration config) {
      var presets = new List<PresetDto>();
      List<DomePalette> stored = config.domePalettes;
      if (stored != null) {
        foreach (DomePalette palette in stored) {
          if (palette == null || palette.Name == null) {
            continue;
          }
          presets.Add(new PresetDto {
            name = palette.Name,
            colors = ToSlots(palette.Colors),
          });
        }
      }
      return presets;
    }

    private static List<SlotDto> ToSlots(LEDColor[] colors) {
      var slots = new List<SlotDto>(PaletteService.LiveSlots);
      for (int i = 0; i < PaletteService.LiveSlots; i++) {
        LEDColor color = colors != null && i < colors.Length ? colors[i] : null;
        slots.Add(ToSlot(color));
      }
      return slots;
    }

    private static SlotDto ToSlot(LEDColor color) {
      if (color == null) {
        return new SlotDto { start = null, end = null };
      }
      return new SlotDto {
        start = ToHex(color.Color1),
        end = color.IsGradient ? ToHex(color.Color2) : null,
      };
    }

    // Map up to eight incoming slots to an LEDColor[8]; extra entries are
    // ignored, missing ones become empty slots. Throws ArgumentException on a
    // malformed hex string.
    private static LEDColor[] ToColors(List<SlotDto> slots) {
      var colors = new LEDColor[PaletteService.LiveSlots];
      for (int i = 0; i < PaletteService.LiveSlots && i < slots.Count; i++) {
        colors[i] = ToColor(slots[i]);
      }
      return colors;
    }

    private static LEDColor ToColor(SlotDto slot) {
      if (slot == null || string.IsNullOrEmpty(slot.start)) {
        return null;
      }
      int start = FromHex(slot.start);
      if (string.IsNullOrEmpty(slot.end)) {
        return new LEDColor(start);
      }
      return new LEDColor(start, FromHex(slot.end));
    }

    private static string ToHex(int rgb) {
      return "#" + (rgb & 0xFFFFFF).ToString("x6");
    }

    private static int FromHex(string value) {
      string hex = value.StartsWith("#") ? value.Substring(1) : value;
      if (hex.Length != 6) {
        throw new ArgumentException("color must be #rrggbb: " + value);
      }
      try {
        return Convert.ToInt32(hex, 16) & 0xFFFFFF;
      } catch (Exception) {
        throw new ArgumentException("color must be #rrggbb: " + value);
      }
    }
  }
}

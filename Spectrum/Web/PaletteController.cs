using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Spectrum.Base;

namespace Spectrum.Web {

  // REST/SSE projection of the named live palettes. Every palette owns its
  // eight editable slots; selecting a palette in either UI only chooses which
  // object to edit and never copies colors through an Apply operation.
  public sealed class PaletteController {
    public sealed class SlotDto {
      public string start { get; set; }
      public string end { get; set; }
    }

    public sealed class PaletteDto {
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

    public object State() => new { palettes = BuildPalettes(this.config) };

    public async Task<(bool ok, string error)> SetColorsAsync(
      string name, List<SlotDto> colors
    ) {
      if (colors == null) {
        return (false, "body must be {\"colors\": [ ... ]}");
      }
      LEDColor[] converted;
      try {
        converted = ToColors(colors);
      } catch (ArgumentException e) {
        return (false, e.Message);
      }
      (bool ok, string error) result = (false, "not run");
      await this.gateway.InvokeAsync(
        () => result = this.service.ReplaceColors(name, converted));
      return result;
    }

    public async Task<(bool ok, string error)> AddAsync(
      string name, string sourceName
    ) {
      (bool ok, string error) result = (false, "not run");
      await this.gateway.InvokeAsync(() => {
        LEDColor[] colors = Find(this.config.domePalettes, sourceName)?.Colors;
        result = this.service.Add(name, colors);
      });
      return result;
    }

    public async Task<(bool ok, string error)> RenameAsync(
      string name, string newName
    ) {
      (bool ok, string error) result = (false, "not run");
      await this.gateway.InvokeAsync(
        () => result = this.service.Rename(name, newName));
      return result;
    }

    public async Task<(bool ok, string error)> DeleteAsync(string name) {
      (bool ok, string error) result = (false, "not run");
      await this.gateway.InvokeAsync(() => result = this.service.Delete(name));
      return result;
    }

    public static List<PaletteDto> BuildPalettes(Configuration config) {
      var result = new List<PaletteDto>();
      if (config.domePalettes != null) {
        foreach (DomePalette palette in config.domePalettes) {
          if (palette == null || string.IsNullOrWhiteSpace(palette.Name)) {
            continue;
          }
          result.Add(new PaletteDto {
            name = palette.Name,
            colors = ToSlots(palette.Colors),
          });
        }
      }
      return result;
    }

    private static DomePalette Find(
      List<DomePalette> palettes, string name
    ) {
      if (palettes == null || name == null) {
        return null;
      }
      foreach (DomePalette palette in palettes) {
        if (palette != null && string.Equals(
              palette.Name, name, StringComparison.OrdinalIgnoreCase)) {
          return palette;
        }
      }
      return null;
    }

    private static List<SlotDto> ToSlots(LEDColor[] colors) {
      var slots = new List<SlotDto>(DomePalette.SlotCount);
      for (int i = 0; i < DomePalette.SlotCount; i++) {
        LEDColor color = colors != null && i < colors.Length ? colors[i] : null;
        slots.Add(color == null
          ? new SlotDto { start = null, end = null }
          : new SlotDto {
              start = ToHex(color.Color1),
              end = color.IsGradient ? ToHex(color.Color2) : null,
            });
      }
      return slots;
    }

    private static LEDColor[] ToColors(List<SlotDto> slots) {
      var colors = new LEDColor[DomePalette.SlotCount];
      for (int i = 0; i < DomePalette.SlotCount && i < slots.Count; i++) {
        SlotDto slot = slots[i];
        if (slot == null || string.IsNullOrEmpty(slot.start)) {
          continue;
        }
        int start = FromHex(slot.start);
        colors[i] = string.IsNullOrEmpty(slot.end)
          ? new LEDColor(start)
          : new LEDColor(start, FromHex(slot.end));
      }
      return colors;
    }

    private static string ToHex(int rgb) =>
      "#" + (rgb & 0xFFFFFF).ToString("x6");

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

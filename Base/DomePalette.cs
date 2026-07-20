using System;
using System.ComponentModel;
using System.Xml.Serialization;

namespace Spectrum.Base {

  // One named, live palette. Layers select an entry in Configuration.domePalettes
  // directly; there is no separate bank or preset-apply step. Colors always
  // represents the eight relative color slots consumed by palette-aware layers.
  public class DomePalette : INotifyPropertyChanged {
    public const int SlotCount = 8;

    public event PropertyChangedEventHandler PropertyChanged;

    public string Name { get; set; }

    // Public for XML serialization. Mutating callers should use the indexer or
    // ReplaceColors so native bindings and web clients receive one change event.
    public LEDColor[] Colors { get; set; }

    public int GetSingleColor(int index) {
      LEDColor color = ColorAt(index);
      return color == null ? 0x000000 : color.Color1;
    }

    public int GetGradientColor(
      int index, double pixelPos, double focusPos, bool wrap
    ) {
      LEDColor color = ColorAt(index);
      if (color == null) {
        return 0x000000;
      }
      return color.IsGradient
        ? color.GradientColor(pixelPos, focusPos, wrap)
        : color.Color1;
    }

    public void ReplaceColors(LEDColor[] values) {
      this.Colors = CopyColors(values);
      this.CallPropertyChanged();
    }

    public static LEDColor[] CopyColors(LEDColor[] source) {
      var copy = new LEDColor[SlotCount];
      for (int i = 0; i < SlotCount; i++) {
        LEDColor color = source != null && i < source.Length ? source[i] : null;
        copy[i] = color == null ? null : new LEDColor(color);
      }
      return copy;
    }

    // Native color pickers bind to this two-dimensional indexer: the first
    // coordinate is the palette slot and the second is the gradient endpoint.
    [XmlIgnore]
    public int? this[int index, int whichColor] {
      get {
        LEDColor color = ColorAt(index);
        if (color == null) {
          return null;
        }
        if (whichColor == 0) {
          return color.Color1;
        }
        if (whichColor == 1) {
          return color.IsGradient ? color.Color2 : (int?)null;
        }
        throw new ArgumentOutOfRangeException(nameof(whichColor));
      }
      set {
        ValidateIndex(index);
        EnsureColors();
        LEDColor current = this.Colors[index];
        if (whichColor == 0) {
          if (!value.HasValue) {
            this.Colors[index] = null;
          } else if (current != null && current.IsGradient) {
            this.Colors[index] = new LEDColor(value.Value, current.Color2);
          } else {
            this.Colors[index] = new LEDColor(value.Value);
          }
        } else if (whichColor == 1) {
          int start = current == null ? 0x000000 : current.Color1;
          this.Colors[index] = value.HasValue
            ? new LEDColor(start, value.Value)
            : new LEDColor(start);
        } else {
          throw new ArgumentOutOfRangeException(nameof(whichColor));
        }
        this.CallPropertyChanged();
      }
    }

    private LEDColor ColorAt(int index) {
      return index >= 0 && index < SlotCount &&
        this.Colors != null && index < this.Colors.Length
          ? this.Colors[index]
          : null;
    }

    private void EnsureColors() {
      if (this.Colors == null || this.Colors.Length != SlotCount) {
        this.Colors = CopyColors(this.Colors);
      }
    }

    private static void ValidateIndex(int index) {
      if (index < 0 || index >= SlotCount) {
        throw new ArgumentOutOfRangeException(nameof(index));
      }
    }

    private void CallPropertyChanged() {
      this.PropertyChanged?.Invoke(
        this, new PropertyChangedEventArgs("Item[]"));
      this.PropertyChanged?.Invoke(
        this, new PropertyChangedEventArgs(nameof(Colors)));
    }
  }

}

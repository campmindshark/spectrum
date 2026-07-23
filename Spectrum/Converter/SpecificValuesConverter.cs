using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace Spectrum {

  class SpecificValuesConverter<F, T> : IValueConverter
      where F : notnull
      where T : notnull {

    private readonly Dictionary<F, T> converterDictionary;
    private readonly Dictionary<T, F>? invertedDictionary;

    // Note that to be bidirectional, values must be unique
    public SpecificValuesConverter(Dictionary<F, T> converterDictionary, bool bidirectional = false) {
      this.converterDictionary = converterDictionary ??
        throw new ArgumentNullException(nameof(converterDictionary));
      this.invertedDictionary = bidirectional
        ? converterDictionary.ToDictionary(x => x.Value, x => x.Key)
        : null;
    }

    public object? Convert(
      object? value,
      Type targetType,
      object? parameter,
      System.Globalization.CultureInfo culture
    ) {
      if (value is not F castValue) {
        return value;
      }
      return this.converterDictionary.TryGetValue(castValue, out T? converted)
        ? converted
        : value;
    }

    public object? ConvertBack(
      object? value,
      Type targetType,
      object? parameter,
      System.Globalization.CultureInfo culture
    ) {
      if (this.invertedDictionary == null) {
        return value;
      }
      if (value is not T castValue) {
        return value;
      }
      return this.invertedDictionary.TryGetValue(
        castValue, out F? converted)
        ? converted
        : value;
    }

  }

}

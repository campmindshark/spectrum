using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace Spectrum
{

    class SpecificValuesConverter<F, T> : IValueConverter
    {

        private Dictionary<F, T> converterDictionary;
        private Dictionary<T, F> invertedDictionary;

        // Note that to be bidirectional, values must be unique
        public SpecificValuesConverter(Dictionary<F, T> converterDictionary, bool bidirectional = false)
        {
            this.converterDictionary = converterDictionary;
            this.invertedDictionary = bidirectional
              ? converterDictionary.ToDictionary(x => x.Value, x => x.Key)
              : null;
        }

        public object Convert(
          object value,
          Type targetType,
          object parameter,
          System.Globalization.CultureInfo culture
        )
        {
            if (!(value is F))
            {
                return value;
            }
            F castValue = (F)value;
            if (!converterDictionary.ContainsKey(castValue))
            {
                return value;
            }
            return converterDictionary[castValue];
        }

        public object ConvertBack(
          object value,
          Type targetType,
          object parameter,
          System.Globalization.CultureInfo culture
        )
        {
            if (this.invertedDictionary == null)
            {
                return value;
            }
            if (!(value is T))
            {
                return value;
            }
            T castValue = (T)value;
            if (!invertedDictionary.ContainsKey(castValue))
            {
                return value;
            }
            return invertedDictionary[castValue];
        }

    }

}
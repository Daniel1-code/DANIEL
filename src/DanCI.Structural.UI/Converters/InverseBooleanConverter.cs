using System;
using System.Globalization;
using System.Windows.Data;

namespace DanCI.Structural.UI.Converters
{
    /// <summary>Inverse un booleen : sert a griser les champs pilotes par un mode automatique.</summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool && (bool)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool && (bool)value);
        }
    }
}

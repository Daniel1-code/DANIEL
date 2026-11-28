using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DanCI.BOQ.UI.ViewModels
{
    /// <summary>Chemin texte (syntaxe WPF / SVG) vers Geometry ; texte vide = rien a dessiner.</summary>
    public sealed class PathGeometryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = value as string;
            if (string.IsNullOrWhiteSpace(s)) return Geometry.Empty;
            try
            {
                Geometry g = Geometry.Parse(s);
                g.Freeze();
                return g;
            }
            catch (FormatException)
            {
                return Geometry.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

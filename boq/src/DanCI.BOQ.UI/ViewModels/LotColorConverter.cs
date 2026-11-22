using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DanCI.BOQ.Core.Dashboard;

namespace DanCI.BOQ.UI.ViewModels
{
    /// <summary>« LOT 03 – Gros œuvre… » ou « 03 » vers la couleur du lot (meme palette que le PDF et Excel).</summary>
    public sealed class LotColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = (value as string ?? string.Empty).Trim();
            if (text.StartsWith("LOT ", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(4);
            }
            int end = 0;
            while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != '–' && text[end] != '-')
            {
                end++;
            }
            string color = Palette.ForLot(text.Substring(0, end));
            if (DanCI.BOQ.UI.Themes.ThemeColors.IsClassic)
            {
                color = DanCI.BOQ.Core.Model.InkSet.Darken(color);   // lisible sur fond clair
            }
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}

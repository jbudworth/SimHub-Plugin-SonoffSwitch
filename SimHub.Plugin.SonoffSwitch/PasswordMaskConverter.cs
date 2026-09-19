using System;
using System.Globalization;
using System.Windows.Data;

namespace SimHub.Plugin.SonoffSwitch
{
    /// <summary>
    /// Renders any non-empty password as a fixed-length bullet mask (rather than dots
    /// matching the actual length, which would leak how many characters it has). Used by
    /// the settings grid's read-only Password column; actual entry happens through
    /// <see cref="PasswordDialog"/>, not inline in the grid.
    /// </summary>
    public class PasswordMaskConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            return string.IsNullOrEmpty(text) ? string.Empty : "\u2022\u2022\u2022\u2022\u2022\u2022";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

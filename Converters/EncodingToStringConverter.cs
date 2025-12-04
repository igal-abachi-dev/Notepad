using System;
using System.Globalization;
using System.Text;
using Avalonia.Data.Converters;

namespace NotepadAvalonia.Converters;

public class EncodingToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Encoding encoding)
        {
            return encoding.WebName.ToUpperInvariant();
        }

        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

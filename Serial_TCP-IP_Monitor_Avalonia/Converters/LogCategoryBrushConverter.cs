using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Serial_TCP_IP_Monitor_Avalonia.Models;

namespace Serial_TCP_IP_Monitor_Avalonia.Converters;

public sealed class LogCategoryBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        LogCategory.Receive => new SolidColorBrush(Color.Parse("#4ADE80")),
        LogCategory.Transmit => new SolidColorBrush(Color.Parse("#60A5FA")),
        LogCategory.Error => new SolidColorBrush(Color.Parse("#FB7185")),
        _ => new SolidColorBrush(Color.Parse("#94A3B8"))
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

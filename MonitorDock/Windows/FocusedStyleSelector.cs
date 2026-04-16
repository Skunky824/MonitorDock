using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MonitorDock.Windows;

public class FocusedStyleSelector : IValueConverter
{
    public Style? NormalStyle { get; set; }
    public Style? FocusedStyle { get; set; }

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true)
            return FocusedStyle;
        return NormalStyle;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

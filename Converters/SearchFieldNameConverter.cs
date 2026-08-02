using System.Globalization;
using System.Windows.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;

namespace CameywareOrder.Converters;

/// <summary>
/// Names an <see cref="OrderSearchField"/> for the search-scope picker.
/// </summary>
/// <remarks>
/// The key is composed (<c>Search.Field.&lt;value&gt;</c>) rather than switched, exactly as
/// <c>PaymentMethod</c> and <c>ServiceType</c> already are — which makes the keys invisible to a
/// literal grep. They are listed in the language files' own section and covered by the "a key is
/// unused only if its prefix is not one the code interpolates" rule; deleting one would leave the
/// picker showing "Search.Field.Phone" rather than failing.
/// </remarks>
[ValueConversion(typeof(OrderSearchField), typeof(string))]
public class SearchFieldNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is OrderSearchField field
            ? LocalizationService.Instance[$"Search.Field.{field}"]
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

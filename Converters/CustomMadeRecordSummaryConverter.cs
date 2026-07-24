using System.Globalization;
using System.Windows.Data;
using LeeYongeOrdering.Localization;
using LeeYongeOrdering.Models;

namespace LeeYongeOrdering.Converters;

[ValueConversion(typeof(CustomMadeServiceRecord), typeof(string))]
public class CustomMadeRecordSummaryConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CustomMadeServiceRecord record)
            return string.Empty;

        var mode = LocalizationService.Instance[$"OrderEdit.Panel.{record.ServiceMode}"];
        var ageType = LocalizationService.Instance[$"AgeType.{record.AgeType}"];
        var items = string.Join(
            ", ",
            new[]
            {
                SectionName("Measure.Section.Jacket", record.JacketLength, record.JacketChest, record.JacketSitAround, record.JacketSleeves),
                SectionName("Measure.Section.Shirt", record.ShirtLength, record.ShirtChest, record.ShirtSitAround, record.ShirtSleeves)
            }.Where(part => !string.IsNullOrWhiteSpace(part)));

        var summary = string.IsNullOrWhiteSpace(items)
            ? $"{record.CustomerName} | {mode} | {ageType}"
            : $"{record.CustomerName} | {mode} | {ageType} | {items}";

        var imageCount = record.Documents?.Count ?? 0;
        if (imageCount > 0)
            summary += "    " + LocalizationService.Instance.Format("CustomMade.Records.ImageCount", imageCount);

        return summary;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static string? SectionName(string sectionKey, params string?[] values)
        => values.Any(part => !string.IsNullOrWhiteSpace(part))
            ? LocalizationService.Instance[sectionKey]
            : null;
}
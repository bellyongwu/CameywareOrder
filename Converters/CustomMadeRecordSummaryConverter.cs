using System.Globalization;
using System.Windows.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;

namespace CameywareOrder.Converters;

[ValueConversion(typeof(CustomMadeServiceRecord), typeof(string))]
public class CustomMadeRecordSummaryConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CustomMadeServiceRecord record)
            return string.Empty;

        var mode = LocalizationService.Instance[$"OrderEdit.Panel.{record.ServiceMode}"];
        var ageType = LocalizationService.Instance[$"AgeType.{record.AgeType}"];
        var items = string.Join(", ", BuildGarmentNames(record));

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

    private static IEnumerable<string> BuildGarmentNames(CustomMadeServiceRecord record)
    {
        if (record.Garments.Count > 0)
        {
            var languageCode = LocalizationService.Instance.CurrentLanguageCode;
            return record.Garments
                .Where(g => g.Values.Any(v => !string.IsNullOrWhiteSpace(v.Cm) || !string.IsNullOrWhiteSpace(v.In)))
                .Select(g => MeasurementTermsService.Instance.ResolveGarmentName(g.GarmentId, languageCode))
                .ToList();
        }

        // Fall back to the legacy static fields for records saved before the
        // garment-driven measurement system.
        return new[]
        {
            SectionName("Measure.Garment.jacket", record.JacketLength, record.JacketChest, record.JacketSitAround, record.JacketSleeves),
            SectionName("Measure.Garment.shirt", record.ShirtLength, record.ShirtChest, record.ShirtSitAround, record.ShirtSleeves)
        }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!);
    }

    private static string? SectionName(string sectionKey, params string?[] values)
        => values.Any(part => !string.IsNullOrWhiteSpace(part))
            ? LocalizationService.Instance[sectionKey]
            : null;
}
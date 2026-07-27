using CameywareOrder.Models;

namespace CameywareOrder.Services;

// Reads custom-made measurement data straight off an order's saved records (rather than
// the live editor) so the main window can render the "定制服务" list flag and print all
// garment measurements. Names are resolved through the Measurement Terms system in the
// requested language; values are taken in the requested unit (cm or inch).
public static class CustomMadeMeasurementReader
{
    // Distinct garment display names across every custom-made record on the order,
    // preserving first-seen order. Only garments that actually carry a value are listed.
    public static List<string> GetGarmentNames(Order order, string languageCode)
        => GetGarmentNames(order.CustomMadeRecords, languageCode);

    // Same, straight off a record list — used by the order editor, which holds unsaved
    // records rather than an Order.
    public static List<string> GetGarmentNames(IEnumerable<CustomMadeServiceRecord> records, string languageCode)
    {
        var service = MeasurementTermsService.Instance;
        var names = new List<string>();

        foreach (var record in records)
        {
            foreach (var garment in record.Garments)
            {
                var hasValue = garment.Values.Any(value =>
                    !string.IsNullOrWhiteSpace(value.Cm) || !string.IsNullOrWhiteSpace(value.In));
                if (!hasValue)
                    continue;

                var garmentType = service.FindGarment(garment.GarmentId);
                if (garmentType is null)
                    continue;

                var name = MeasurementTermsService.ResolveGarmentName(garmentType, languageCode);
                if (!names.Contains(name))
                    names.Add(name);
            }
        }

        return names;
    }

    // One printable section per garment on a record: the garment name plus its term/value
    // rows, ordered by the garment's configured term order, in the requested unit.
    public static List<(string Title, List<(string Label, string Value)> Rows)> BuildSections(
        CustomMadeServiceRecord record, string languageCode, bool isInch)
    {
        var sections = new List<(string Title, List<(string Label, string Value)> Rows)>();

        foreach (var garment in record.Garments)
        {
            if (BuildGarmentSection(garment, languageCode, isInch) is { } section)
                sections.Add(section);
        }

        return sections;
    }

    private static (string Title, List<(string Label, string Value)> Rows)? BuildGarmentSection(
        GarmentMeasurement garment, string languageCode, bool isInch)
    {
        var service = MeasurementTermsService.Instance;
        var garmentType = service.FindGarment(garment.GarmentId);
        if (garmentType is null)
            return null;

        var valueByTerm = garment.Values
            .GroupBy(value => value.TermId)
            .ToDictionary(group => group.Key, group => group.First());

        var rows = new List<(string Label, string Value)>();
        foreach (var term in service.GetGarmentTerms(garment.GarmentId))
        {
            if (!valueByTerm.TryGetValue(term.Id, out var value))
                continue;

            var display = isInch ? value.In : value.Cm;
            if (string.IsNullOrWhiteSpace(display))
                continue;

            rows.Add((MeasurementTermsService.ResolveTermName(term, languageCode), display.Trim()));
        }

        if (rows.Count == 0)
            return null;

        return (MeasurementTermsService.ResolveGarmentName(garmentType, languageCode), rows);
    }
}

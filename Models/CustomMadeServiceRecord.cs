using System.Linq;

namespace LeeYongeOrdering.Models;

public enum CustomMadeServiceMode
{
    MeasurementsOnly = 1,
    CustomFromScratch = 2
}

public enum CustomMadeAgeType
{
    AdultMale = 1,
    AdultFemale = 2,
    TeenBoy = 3,
    TeenGirl = 4,
    ChildBoy = 5,
    ChildGirl = 6
}

public class CustomMadeServiceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CustomMadeServiceMode ServiceMode { get; set; } = CustomMadeServiceMode.CustomFromScratch;
    public string CustomerName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public CustomMadeAgeType AgeType { get; set; } = CustomMadeAgeType.AdultMale;
    public string? JacketLength { get; set; }
    public string? JacketChest { get; set; }
    public string? JacketSitAround { get; set; }
    public string? JacketSleeves { get; set; }
    public string? ShirtLength { get; set; }
    public string? ShirtChest { get; set; }
    public string? ShirtSitAround { get; set; }
    public string? ShirtSleeves { get; set; }
    public decimal? Price { get; set; }
    public decimal? TaxRate { get; set; }

    public decimal Subtotal => Price ?? 0m;

    public decimal SumTotal => Subtotal + (Subtotal * (TaxRate ?? 0m) / 100m);

    public string MeasurementsSummary
        => string.Join(" | ", new[]
        {
            FormatMeasurement("Jacket L", JacketLength),
            FormatMeasurement("Jacket Chest", JacketChest),
            FormatMeasurement("Jacket Sit", JacketSitAround),
            FormatMeasurement("Jacket Sleeves", JacketSleeves),
            FormatMeasurement("Shirt L", ShirtLength),
            FormatMeasurement("Shirt Chest", ShirtChest),
            FormatMeasurement("Shirt Sit", ShirtSitAround),
            FormatMeasurement("Shirt Sleeves", ShirtSleeves)
        }.Where(part => !string.IsNullOrWhiteSpace(part))!);

    public string DisplaySummary
        => string.IsNullOrWhiteSpace(MeasurementsSummary)
            ? $"{CustomerName} | {AgeType} | {ServiceMode}"
            : $"{CustomerName} | {AgeType} | {ServiceMode} | {MeasurementsSummary}";

    public override string ToString()
        => DisplaySummary;

    private static string? FormatMeasurement(string label, string? value)
        => string.IsNullOrWhiteSpace(value) ? null : $"{label}: {value.Trim()}";
}

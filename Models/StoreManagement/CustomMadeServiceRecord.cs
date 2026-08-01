using System.Linq;

namespace CameywareOrder.Models;

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
    public string? JacketLengthCm { get; set; }
    public string? JacketLengthIn { get; set; }
    public string? JacketChestCm { get; set; }
    public string? JacketChestIn { get; set; }
    public string? JacketSitAroundCm { get; set; }
    public string? JacketSitAroundIn { get; set; }
    public string? JacketSleevesCm { get; set; }
    public string? JacketSleevesIn { get; set; }
    public string? ShirtLengthCm { get; set; }
    public string? ShirtLengthIn { get; set; }
    public string? ShirtChestCm { get; set; }
    public string? ShirtChestIn { get; set; }
    public string? ShirtSitAroundCm { get; set; }
    public string? ShirtSitAroundIn { get; set; }
    public string? ShirtSleevesCm { get; set; }
    public string? ShirtSleevesIn { get; set; }
    public decimal? Price { get; set; }
    public decimal? TaxRate { get; set; }

    // Dynamic, garment-driven measurements. Replaces the static Jacket/Shirt fields
    // above (kept only for backward compatibility with records saved before the
    // Measurement Terms system). Each selected garment carries the values for the
    // measurement terms mapped to it, storing both units so switching is lossless.
    public List<GarmentMeasurement> Garments { get; set; } = new();

    public List<CustomMadeDocument> Documents { get; set; } = new();

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

/// <summary>
/// The measurements captured for one selected garment on a custom-made record.
/// </summary>
public class GarmentMeasurement
{
    public string GarmentId { get; set; } = string.Empty;

    public List<MeasurementValue> Values { get; set; } = new();
}

/// <summary>
/// A single measurement value for a term, stored in both units so that switching
/// between centimeters and inches never loses the originally entered figure.
/// </summary>
public class MeasurementValue
{
    public string TermId { get; set; } = string.Empty;

    public string? Cm { get; set; }

    public string? In { get; set; }
}

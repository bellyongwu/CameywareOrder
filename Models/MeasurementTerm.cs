namespace LeeYongeOrdering.Models;

/// <summary>
/// A single measurement term used when taking bespoke measurements (e.g. Chest,
/// Waist, Sleeve). Predefined terms are locked: their names come from the string
/// table (key <c>Measure.Term.&lt;Id&gt;</c>) and cannot be edited or deleted.
/// User-added terms carry per-language name overrides in <see cref="Names"/> so a
/// region can name the same measurement differently ("afterwords overriding").
/// </summary>
public class MeasurementTerm
{
    public string Id { get; set; } = string.Empty;

    public bool IsPredefined { get; set; }

    /// <summary>Language code → display name. Only used for user-added terms.</summary>
    public Dictionary<string, string> Names { get; set; } = new();
}

/// <summary>
/// A garment type (e.g. Jacket) together with the ordered list of measurement term
/// ids assigned to it. Predefined garments are locked (names come from the string
/// table, key <c>Measure.Garment.&lt;Id&gt;</c>); user-added garments carry their
/// own per-language names and may be edited/deleted.
/// </summary>
public class GarmentType
{
    public string Id { get; set; } = string.Empty;

    public bool IsPredefined { get; set; }

    /// <summary>Language code → display name. Only used for user-added garments.</summary>
    public Dictionary<string, string> Names { get; set; } = new();

    /// <summary>Ordered ids of the measurement terms mapped to this garment.</summary>
    public List<string> TermIds { get; set; } = new();
}

/// <summary>
/// Persisted configuration of the Measurement Terms system: the master list of
/// measurement terms and the garment → term mappings. Serialized to JSON under the
/// app's local AppData folder (mirrors the other global-config stores).
/// </summary>
public class MeasurementTermsConfig
{
    public List<MeasurementTerm> Terms { get; set; } = new();

    public List<GarmentType> Garments { get; set; } = new();
}

/// <summary>
/// Static definitions of the predefined (locked) measurement terms and garments,
/// plus their default garment → term mappings. These seed a fresh configuration and
/// are also merged into an existing configuration on load so a new app version can
/// introduce additional predefined terms/garments without clobbering user edits.
/// </summary>
public static class MeasurementTermDefaults
{
    // Term ids reused across the predefined list and the default garment mappings;
    // named to keep the tables literal-free (avoids duplicated string literals).
    private const string Length = "length";
    private const string Chest = "chest";
    private const string Waist = "waist";
    private const string Hip = "hip";
    private const string Shoulder = "shoulder";
    private const string Sleeve = "sleeve";
    private const string SitAround = "sitAround";
    private const string Neck = "neck";
    private const string Bust = "bust";
    private const string UnderBust = "underBust";

    /// <summary>Ordered ids of every predefined measurement term (bespoke body sections).</summary>
    public static readonly IReadOnlyList<string> PredefinedTermIds = new[]
    {
        Length, Chest, Waist, Hip, Shoulder, Sleeve, SitAround,
        Neck, "back", "bicep", "cuff", Bust, UnderBust, "inseam", "outseam",
        "thigh", "knee", "bottom", "rise", "frontLength", "armhole"
    };

    /// <summary>Ordered ids of every predefined garment type.</summary>
    public static readonly IReadOnlyList<string> PredefinedGarmentIds = new[]
    {
        "jacket", "vest", "shirt", "pants", "blouse", "dress", "qipao"
    };

    /// <summary>Default measurement terms assigned to each predefined garment.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> DefaultGarmentTerms =
        new Dictionary<string, string[]>
        {
            ["jacket"] = new[] { Length, Chest, Waist, Shoulder, Sleeve, SitAround },
            ["vest"] = new[] { Length, Chest, Waist, Shoulder, SitAround },
            ["shirt"] = new[] { Length, Chest, Shoulder, Sleeve, Neck, "cuff", SitAround },
            ["pants"] = new[] { Waist, Hip, "inseam", "outseam", "thigh", "knee", "bottom", "rise" },
            ["blouse"] = new[] { Length, Bust, Waist, Shoulder, Sleeve, Neck },
            ["dress"] = new[] { Length, Bust, UnderBust, Waist, Hip, Shoulder, Sleeve },
            ["qipao"] = new[] { Length, Bust, UnderBust, Waist, Hip, Shoulder, Neck, Sleeve }
        };

    public static MeasurementTermsConfig CreateDefaultConfig()
    {
        var config = new MeasurementTermsConfig();

        foreach (var termId in PredefinedTermIds)
            config.Terms.Add(new MeasurementTerm { Id = termId, IsPredefined = true });

        foreach (var garmentId in PredefinedGarmentIds)
        {
            config.Garments.Add(new GarmentType
            {
                Id = garmentId,
                IsPredefined = true,
                TermIds = DefaultGarmentTerms.TryGetValue(garmentId, out var terms)
                    ? new List<string>(terms)
                    : new List<string>()
            });
        }

        return config;
    }

    /// <summary>
    /// True when the term is part of a predefined garment's original locked mapping,
    /// so it must not be removed from that garment in the mapping UI.
    /// </summary>
    public static bool IsTermLockedInGarment(GarmentType garment, string termId)
        => garment.IsPredefined
           && DefaultGarmentTerms.TryGetValue(garment.Id, out var terms)
           && terms.Contains(termId);
}

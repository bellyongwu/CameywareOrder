using System.IO;
using System.Text.Json;
using LeeYongeOrdering.Localization;
using LeeYongeOrdering.Models;
using Path = System.IO.Path;

namespace LeeYongeOrdering.Services;

/// <summary>
/// Owns the Measurement Terms configuration: the master list of measurement terms
/// and the garment → term mappings. Predefined terms/garments are locked (names come
/// from the string table and cannot be edited/deleted); user-added terms/garments
/// carry per-language name overrides and are fully editable. The configuration is
/// persisted as JSON under the app's local AppData folder, mirroring the resilient,
/// non-fatal persistence style of the other global-config stores.
/// </summary>
public sealed class MeasurementTermsService
{
    public static MeasurementTermsService Instance { get; } = new();

    private const string FileName = "measurement-terms.json";

    private readonly MeasurementTermsConfig _config;

    private MeasurementTermsService()
    {
        _config = LoadOrSeed();
    }

    public event EventHandler? ConfigChanged;

    public MeasurementTermsConfig Config => _config;

    public IReadOnlyList<GarmentType> Garments => _config.Garments;

    public IReadOnlyList<MeasurementTerm> Terms => _config.Terms;

    public MeasurementTerm? FindTerm(string termId)
        => _config.Terms.FirstOrDefault(t => string.Equals(t.Id, termId, StringComparison.Ordinal));

    public GarmentType? FindGarment(string garmentId)
        => _config.Garments.FirstOrDefault(g => string.Equals(g.Id, garmentId, StringComparison.Ordinal));

    /// <summary>Resolves a term's display name for the current UI language.</summary>
    public static string ResolveTermName(MeasurementTerm term)
        => ResolveTermName(term, LocalizationService.Instance.CurrentLanguageCode);

    /// <summary>Resolves a term's display name for a specific language code.</summary>
    public static string ResolveTermName(MeasurementTerm term, string languageCode)
    {
        if (term.IsPredefined)
            return LocalizationService.Instance.GetText($"Measure.Term.{term.Id}", languageCode);

        return ResolveOverrideName(term.Names, languageCode, term.Id);
    }

    public string ResolveTermName(string termId, string languageCode)
    {
        var term = FindTerm(termId);
        return term is null ? termId : ResolveTermName(term, languageCode);
    }

    /// <summary>Resolves a garment's display name for the current UI language.</summary>
    public static string ResolveGarmentName(GarmentType garment)
        => ResolveGarmentName(garment, LocalizationService.Instance.CurrentLanguageCode);

    /// <summary>Resolves a garment's display name for a specific language code.</summary>
    public static string ResolveGarmentName(GarmentType garment, string languageCode)
    {
        if (garment.IsPredefined)
            return LocalizationService.Instance.GetText($"Measure.Garment.{garment.Id}", languageCode);

        return ResolveOverrideName(garment.Names, languageCode, garment.Id);
    }

    public string ResolveGarmentName(string garmentId, string languageCode)
    {
        var garment = FindGarment(garmentId);
        return garment is null ? garmentId : ResolveGarmentName(garment, languageCode);
    }

    private static string ResolveOverrideName(IReadOnlyDictionary<string, string> names, string languageCode, string fallback)
    {
        if (names.TryGetValue(languageCode, out var exact) && !string.IsNullOrWhiteSpace(exact))
            return exact;

        return names.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? fallback;
    }

    /// <summary>The ordered measurement terms currently mapped to a garment.</summary>
    public IReadOnlyList<MeasurementTerm> GetGarmentTerms(string garmentId)
    {
        var garment = FindGarment(garmentId);
        if (garment is null)
            return Array.Empty<MeasurementTerm>();

        return garment.TermIds
            .Select(FindTerm)
            .Where(term => term is not null)
            .Select(term => term!)
            .ToList();
    }

    // --- Mutations (each persists + notifies) -----------------------------------

    public MeasurementTerm AddCustomTerm(Dictionary<string, string> names, MeasurementGender gender = MeasurementGender.Common)
    {
        var term = new MeasurementTerm
        {
            Id = "custom-" + Guid.NewGuid().ToString("N"),
            IsPredefined = false,
            Names = names,
            Gender = gender
        };
        _config.Terms.Add(term);
        Persist();
        return term;
    }

    public void UpdateCustomTermNames(string termId, Dictionary<string, string> names, MeasurementGender gender)
    {
        var term = FindTerm(termId);
        if (term is null || term.IsPredefined)
            return;

        term.Names = names;
        term.Gender = gender;
        Persist();
    }

    /// <summary>
    /// True when every provided language name matches (trimmed, case-insensitive) the
    /// resolved name of some existing term (predefined or custom, other than
    /// <paramref name="excludeTermId"/>) for that same language — i.e. the exact same
    /// measurement already exists under a different id.
    /// </summary>
    public bool IsDuplicateTermName(IReadOnlyDictionary<string, string> names, string? excludeTermId = null)
    {
        if (names.Count == 0)
            return false;

        return _config.Terms
            .Where(term => !string.Equals(term.Id, excludeTermId, StringComparison.Ordinal))
            .Any(term => names.All(pair =>
                string.Equals(ResolveTermName(term, pair.Key).Trim(), pair.Value.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    public void DeleteCustomTerm(string termId)
    {
        var term = FindTerm(termId);
        if (term is null || term.IsPredefined)
            return;

        _config.Terms.Remove(term);
        foreach (var garment in _config.Garments)
            garment.TermIds.RemoveAll(id => string.Equals(id, termId, StringComparison.Ordinal));

        Persist();
    }

    public GarmentType AddCustomGarment(Dictionary<string, string> names)
    {
        var garment = new GarmentType
        {
            Id = "custom-" + Guid.NewGuid().ToString("N"),
            IsPredefined = false,
            Names = names,
            TermIds = new List<string>()
        };
        _config.Garments.Add(garment);
        Persist();
        return garment;
    }

    public void UpdateCustomGarmentNames(string garmentId, Dictionary<string, string> names)
    {
        var garment = FindGarment(garmentId);
        if (garment is null || garment.IsPredefined)
            return;

        garment.Names = names;
        Persist();
    }

    public void DeleteCustomGarment(string garmentId)
    {
        var garment = FindGarment(garmentId);
        if (garment is null || garment.IsPredefined)
            return;

        _config.Garments.Remove(garment);
        Persist();
    }

    public void AddTermToGarment(string garmentId, string termId)
    {
        var garment = FindGarment(garmentId);
        if (garment is null || FindTerm(termId) is null)
            return;

        if (garment.TermIds.Contains(termId))
            return;

        garment.TermIds.Add(termId);
        Persist();
    }

    public void RemoveTermFromGarment(string garmentId, string termId)
    {
        var garment = FindGarment(garmentId);
        if (garment is null)
            return;

        if (MeasurementTermDefaults.IsTermLockedInGarment(garment, termId))
            return;

        garment.TermIds.RemoveAll(id => string.Equals(id, termId, StringComparison.Ordinal));
        Persist();
    }

    /// <summary>
    /// Switches a predefined garment into "customized measurements" mode: its default
    /// term mapping stops being locked, so terms can be freely added or removed from
    /// it. Its current terms are kept as the starting point. No-op for user-added
    /// garments, which are already fully editable.
    /// </summary>
    public void EnableCustomMeasurements(string garmentId)
    {
        var garment = FindGarment(garmentId);
        if (garment is null || !garment.IsPredefined || garment.UseCustomMeasurements)
            return;

        garment.UseCustomMeasurements = true;
        Persist();
    }

    /// <summary>
    /// Discards a predefined garment's customization and restores its original default
    /// term mapping. No-op for user-added garments.
    /// </summary>
    public void RestoreDefaultMeasurements(string garmentId)
    {
        var garment = FindGarment(garmentId);
        if (garment is null || !garment.IsPredefined || !garment.UseCustomMeasurements)
            return;

        garment.UseCustomMeasurements = false;
        garment.TermIds = MeasurementTermDefaults.DefaultGarmentTerms.TryGetValue(garment.Id, out var terms)
            ? new List<string>(terms)
            : new List<string>();
        Persist();
    }

    // --- Persistence ------------------------------------------------------------

    private void Persist()
    {
        Save(_config);
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string SettingDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LeeYongeOrdering");

    private static string SettingFilePath => Path.Combine(SettingDirectory, FileName);

    private static MeasurementTermsConfig LoadOrSeed()
    {
        var loaded = TryLoad();
        if (loaded is null)
        {
            var seeded = MeasurementTermDefaults.CreateDefaultConfig();
            Save(seeded);
            return seeded;
        }

        // Merge any predefined terms/garments introduced by a newer app version
        // without disturbing existing user customizations.
        if (MergePredefined(loaded))
            Save(loaded);

        return loaded;
    }

    private static bool MergePredefined(MeasurementTermsConfig config)
    {
        var changed = false;

        foreach (var termId in MeasurementTermDefaults.PredefinedTermIds
                     .Where(termId => config.Terms.All(t => !string.Equals(t.Id, termId, StringComparison.Ordinal))))
        {
            config.Terms.Add(new MeasurementTerm
            {
                Id = termId,
                IsPredefined = true,
                Gender = MeasurementTermDefaults.GetPredefinedTermGender(termId)
            });
            changed = true;
        }

        // A predefined term's gender is fixed data owned by MeasurementTermDefaults, not
        // a user edit — so keep it in sync even for terms that were already persisted
        // before this field existed (or before a term's classification was refined).
        // Without this, existing configs would keep every predefined term at its
        // deserialized default (Common) forever, making the gender filter a no-op.
        foreach (var term in config.Terms.Where(t => t.IsPredefined))
        {
            var expectedGender = MeasurementTermDefaults.GetPredefinedTermGender(term.Id);
            if (term.Gender == expectedGender)
                continue;

            term.Gender = expectedGender;
            changed = true;
        }

        foreach (var garmentId in MeasurementTermDefaults.PredefinedGarmentIds
                     .Where(garmentId => config.Garments.All(g => !string.Equals(g.Id, garmentId, StringComparison.Ordinal))))
        {
            config.Garments.Add(new GarmentType
            {
                Id = garmentId,
                IsPredefined = true,
                TermIds = MeasurementTermDefaults.DefaultGarmentTerms.TryGetValue(garmentId, out var terms)
                    ? new List<string>(terms)
                    : new List<string>()
            });
            changed = true;
        }

        return changed;
    }

    private static MeasurementTermsConfig? TryLoad()
    {
        try
        {
            if (!File.Exists(SettingFilePath))
                return null;

            var json = File.ReadAllText(SettingFilePath);
            return JsonSerializer.Deserialize<MeasurementTermsConfig>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void Save(MeasurementTermsConfig config)
    {
        try
        {
            Directory.CreateDirectory(SettingDirectory);
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingFilePath, json);
        }
        catch
        {
            // Persistence is best-effort; a failure to save must not crash the app.
        }
    }
}

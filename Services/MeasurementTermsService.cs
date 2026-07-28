using System.IO;
using System.Text.Json;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using Path = System.IO.Path;
using CameywareOrder.Configuration;

namespace CameywareOrder.Services;

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

    // Legacy per-machine file. Still the source the first shop's terms are adopted from; once a
    // shop is bound the per-shop file below is used instead.
    private const string FileName = "measurement-terms.json";

    // Per-shop file name. Keyed on Shop.PublicId, NEVER Shop.Id: ids are local autoincrement
    // values and whole databases move between machines, so an imported shop would otherwise pick
    // up an unrelated local shop's terms.
    private static string ShopFileName(Shop shop) => $"measurement-terms-{shop.PublicId:N}.json";

    private readonly MeasurementTermsConfig _config;
    private Shop? _shop;

    private MeasurementTermsService()
    {
        _config = LoadOrSeed();
    }

    public event EventHandler? ConfigChanged;

    /// <summary>
    /// Points the service at a shop and loads that shop's terms in place. The config object itself
    /// is never replaced — <see cref="_config"/> is readonly and callers hold references to its
    /// lists — so the contents are swapped exactly the way <see cref="ImportConfig"/> already does.
    /// A shop with no file yet inherits the seeded defaults, which are then written for it.
    /// </summary>
    public void BindTo(Shop shop)
    {
        ArgumentNullException.ThrowIfNull(shop);

        _shop = shop;

        // A shop with no file of its own starts from the predefined defaults. It deliberately does
        // NOT fall back to the legacy file — that would hand every newly created shop the first
        // shop's customizations. The first shop adopts the legacy file explicitly and once, via
        // AdoptLegacyFileFor.
        var loaded = TryLoad(SettingFilePath) ?? MeasurementTermDefaults.CreateDefaultConfig();
        ReplaceConfigInPlace(loaded);
        MergePredefined(_config);
        Persist();
    }

    /// <summary>
    /// One-time migration: gives the first shop the terms this machine already had. Copies rather
    /// than moves, so the pre-multi-shop file stays put as a rollback safety net, and does nothing
    /// if the shop already has its own file.
    /// </summary>
    public static void AdoptLegacyFileFor(Shop shop)
    {
        ArgumentNullException.ThrowIfNull(shop);

        try
        {
            var target = Path.Combine(SettingDirectory, ShopFileName(shop));
            if (File.Exists(target) || !File.Exists(LegacyFilePath))
                return;

            Directory.CreateDirectory(SettingDirectory);
            File.Copy(LegacyFilePath, target);
        }
        catch (IOException)
        {
            // Best-effort, like every other persistence path here: the shop falls back to the
            // seeded defaults rather than the app failing to start.
        }
    }

    /// <summary>
    /// Seeds <paramref name="target"/>'s measurement terms from <paramref name="source"/>'s — the
    /// new-shop wizard's "copy from an existing shop" option. Copies the FILE rather than the
    /// in-memory config, so the source shop does not have to be the open one and the active shop's
    /// binding is left untouched.
    ///
    /// Does nothing when the source has no file yet: the target then falls back to the predefined
    /// defaults when it is bound, which is exactly what the source is showing too.
    /// </summary>
    public static void CopyConfigBetweenShops(Shop source, Shop target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        try
        {
            var from = Path.Combine(SettingDirectory, ShopFileName(source));
            var to = Path.Combine(SettingDirectory, ShopFileName(target));

            // Never overwrite: a shop that already has terms of its own has been configured.
            if (!File.Exists(from) || File.Exists(to))
                return;

            Directory.CreateDirectory(SettingDirectory);
            File.Copy(from, to);
        }
        catch (IOException)
        {
            // Best-effort, like every other persistence path here: the new shop falls back to the
            // seeded defaults rather than the creation failing.
        }
    }

    private void ReplaceConfigInPlace(MeasurementTermsConfig source)
    {
        _config.Terms.Clear();
        _config.Terms.AddRange(source.Terms);
        _config.Garments.Clear();
        _config.Garments.AddRange(source.Garments);
    }

    public MeasurementTermsConfig Config => _config;

    public IReadOnlyList<GarmentType> Garments => _config.Garments;

    public IReadOnlyList<MeasurementTerm> Terms => _config.Terms;

    public MeasurementTerm? FindTerm(string termId)
        => _config.Terms.Find(t => string.Equals(t.Id, termId, StringComparison.Ordinal));
    public GarmentType? FindGarment(string garmentId)
        => _config.Garments.Find(g => string.Equals(g.Id, garmentId, StringComparison.Ordinal));

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

    // --- Import / export ---------------------------------------------------------

    private static readonly JsonSerializerOptions ExportOptions = new() { WriteIndented = true };

    /// <summary>Serializes the current configuration to indented JSON for backup/export.</summary>
    public string ExportConfigJson() => JsonSerializer.Serialize(_config, ExportOptions);

    /// <summary>
    /// Attempts to parse a previously-exported configuration. Returns null (instead of
    /// throwing) on invalid/corrupt JSON so callers can show a friendly "invalid file"
    /// message rather than crashing.
    /// </summary>
    public static MeasurementTermsConfig? TryParseConfigJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<MeasurementTermsConfig>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Replaces the current configuration with an imported one (see
    /// <see cref="ExportConfigJson"/>). Re-runs the predefined merge so a config
    /// exported from an older app version still ends up with today's predefined term
    /// ids / gender classifications, then persists and notifies listeners.
    /// </summary>
    public void ImportConfig(MeasurementTermsConfig config)
    {
        _config.Terms.Clear();
        _config.Terms.AddRange(config.Terms ?? new List<MeasurementTerm>());
        _config.Garments.Clear();
        _config.Garments.AddRange(config.Garments ?? new List<GarmentType>());

        MergePredefined(_config);
        Persist();
    }

    // --- Persistence ------------------------------------------------------------

    private void Persist()
    {
        Save(_config, SettingFilePath);
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Still the data-folder ROOT, not a subfolder. These files are keyed on Shop.PublicId in their
    /// own FILE NAME (measurement-terms-&lt;publicId&gt;.json), and the pre-multi-shop file beside
    /// them is the seed the first shop adopts — moving them would mean migrating a name-keyed set
    /// for no gain the user can see.
    /// </summary>
    private static string SettingDirectory => UserDataPaths.ShopDataDirectory;

    /// <summary>Pre-multi-shop file; the seed the first shop's terms are adopted from.</summary>
    private static string LegacyFilePath => Path.Combine(SettingDirectory, FileName);

    /// <summary>Active file: the bound shop's own, or the legacy one before a shop is open.</summary>
    private string SettingFilePath
        => _shop is null ? LegacyFilePath : Path.Combine(SettingDirectory, ShopFileName(_shop));

    private static MeasurementTermsConfig LoadOrSeed()
    {
        var loaded = TryLoad(LegacyFilePath);
        if (loaded is null)
        {
            var seeded = MeasurementTermDefaults.CreateDefaultConfig();
            Save(seeded, LegacyFilePath);
            return seeded;
        }

        // Merge any predefined terms/garments introduced by a newer app version
        // without disturbing existing user customizations.
        if (MergePredefined(loaded))
            Save(loaded, LegacyFilePath);

        return loaded;
    }

    private static bool MergePredefined(MeasurementTermsConfig config)
    {
        var changed = false;

        foreach (var termId in MeasurementTermDefaults.PredefinedTermIds
                     .Where(termId => config.Terms.TrueForAll(t => !string.Equals(t.Id, termId, StringComparison.Ordinal))))
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
                     .Where(garmentId => config.Garments.TrueForAll(g => !string.Equals(g.Id, garmentId, StringComparison.Ordinal))))
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

    private static MeasurementTermsConfig? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MeasurementTermsConfig>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void Save(MeasurementTermsConfig config, string path)
    {
        try
        {
            Directory.CreateDirectory(SettingDirectory);
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Persistence is best-effort; a failure to save must not crash the app.
        }
    }
}

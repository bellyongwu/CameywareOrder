using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
// HotChocolate contributes a global `Path` type, so this has to be aliased wherever the file
// system's Path is used. Same treatment as CurrencySettingService and ReceiptBrandingStore.
using Path = System.IO.Path;

namespace CameywareOrder.Localization;

public sealed class LocalizationService : INotifyPropertyChanged, ILocalizedText
{
    public static LocalizationService Instance { get; } = new();

    /// <summary>
    /// Keys under Format.* describe how a language PUNCTUATES rather than what it says. They are
    /// looked up through the same table as any other key, so they cost no extra machinery, but they
    /// are named here rather than spelled out at call sites so a typo is a compile error instead of
    /// a separator that silently renders as the key itself.
    /// </summary>
    private const string ListSeparatorKey = "Format.ListSeparator";
    private const string BulletSeparatorKey = "Format.BulletSeparator";

    private readonly Dictionary<string, Dictionary<string, string>> _translations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LanguageOption> _availableLanguages = new();

    private string _defaultLanguageCode = "en-US";
    private string _currentLanguageCode = "en-US";

    private LocalizationService() { }

    public IReadOnlyList<LanguageOption> AvailableLanguages => _availableLanguages;

    /// <summary>
    /// Keys present in some shipped language but missing from others, as of the last load. Empty is
    /// the healthy state. See <see cref="FindKeyGaps"/> for why this is surfaced rather than thrown.
    /// </summary>
    public IReadOnlyList<LanguageKeyGap> KeyGaps { get; private set; } = Array.Empty<LanguageKeyGap>();

    public string CurrentLanguageCode => _currentLanguageCode;

    public string this[string key] => TryGetText(key, out var value) ? value : key;

    /// <summary>
    /// Resolves a key for a specific language code (used e.g. to render a document
    /// in a chosen language without changing the current UI language). Falls back
    /// to the normal current/default resolution when the language or key is missing.
    /// </summary>
    public string GetText(string key, string languageCode)
    {
        if (_translations.TryGetValue(languageCode, out var map)
            && map.TryGetValue(key, out var value)
            && value is not null)
        {
            return value;
        }

        return this[key];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    /// <summary>
    /// Loads every <c>*.lang.xml</c> in a directory — one document per language, discovered rather
    /// than listed, so adding a language is a matter of dropping a file in.
    /// </summary>
    /// <param name="directoryPath">Typically <see cref="Configuration.SystemSettingsPaths.LanguagesDirectory"/>.</param>
    /// <param name="defaultLanguageCode">
    /// Which discovered language is the default and the fallback for a missing key. A value ABOUT
    /// the set, so it cannot live in any one language's file — two of them could each claim it.
    /// Ignored when it does not name a discovered language.
    /// </param>
    public void LoadFromDirectory(string directoryPath, string? defaultLanguageCode)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Language directory not found: {directoryPath}");

        // Ordered so a build is reproducible; the display order is decided in Load, not here.
        var files = Directory
            .GetFiles(directoryPath, "*.lang.xml", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
            throw new InvalidDataException($"No *.lang.xml files found in {directoryPath}.");

        var blocks = new List<LanguageBlock>(files.Count);
        foreach (var file in files)
        {
            var root = XDocument.Load(file).Root;
            if (root is null || !string.Equals(root.Name.LocalName, "Language", StringComparison.Ordinal))
                throw new InvalidDataException($"{Path.GetFileName(file)}: expected a <Language> root element.");

            blocks.Add(new LanguageBlock(root, Path.GetFileName(file)));
        }

        Load(blocks, defaultLanguageCode);
    }

    /// <summary>
    /// Loads the legacy single-file table — one <c>&lt;Languages&gt;</c> root holding every
    /// <c>&lt;Language&gt;</c> block, with the default as an attribute on the root.
    /// </summary>
    /// <remarks>
    /// Kept after the split to <see cref="LoadFromDirectory"/> because it is genuinely the simpler
    /// shape for a test that wants one self-contained table, and because it costs nothing: both
    /// paths share the same core.
    /// </remarks>
    public void LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Language file not found: {filePath}", filePath);

        var root = XDocument.Load(filePath).Root;
        if (root is null || !string.Equals(root.Name.LocalName, "Languages", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid language XML format: missing <Languages> root.");

        var origin = Path.GetFileName(filePath);
        var blocks = root.Elements("Language").Select(node => new LanguageBlock(node, origin)).ToList();

        Load(blocks, (string?)root.Attribute("default"));
    }

    private void Load(IReadOnlyList<LanguageBlock> blocks, string? defaultLanguageCode)
    {
        _translations.Clear();
        _availableLanguages.Clear();

        // Which file first declared each code, so a clash can name both sides rather than whichever
        // happened to be read second.
        var originByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (node, origin) in blocks)
        {
            var code = (string?)node.Attribute("code");
            var name = (string?)node.Attribute("name");

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
                continue;

            // A duplicate code is almost always a language file copied to a new name whose `code`
            // attribute was never changed. Silently letting the second win would mean the new
            // language quietly replacing the one it was copied from, so it is refused outright —
            // the file name is a convention, the code inside is the identity.
            //
            // BOTH files are named, because which one arrives "second" is just alphabetical order:
            // de-DE.lang.xml sorts before en-US.lang.xml, so a bad copy called de-DE that still says
            // code="en-US" would otherwise see the blameless en-US.lang.xml reported as the problem.
            if (originByCode.TryGetValue(code, out var firstOrigin))
            {
                throw new InvalidDataException(
                    $"Language code '{code}' is declared by two files: {firstOrigin} and {origin}. " +
                    "The file name is only a convention — the code attribute inside decides which " +
                    "language a file is, so one of them needs its code changed.");
            }

            originByCode[code] = origin;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var textNode in node.Elements("Text"))
            {
                var key = (string?)textNode.Attribute("key");
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                map[key] = textNode.Value;
            }

            _translations[code] = map;
            _availableLanguages.Add(new LanguageOption(code, name));
        }

        if (_availableLanguages.Count == 0)
            throw new InvalidDataException("No languages found.");

        _defaultLanguageCode = !string.IsNullOrWhiteSpace(defaultLanguageCode)
                               && _translations.ContainsKey(defaultLanguageCode)
            ? defaultLanguageCode
            : _availableLanguages[0].Code;

        SortAvailableLanguages();
        KeyGaps = FindKeyGaps();
        ReportKeyGaps(KeyGaps);

        _currentLanguageCode = _defaultLanguageCode;
        NotifyLanguageChanged();
    }

    /// <summary>
    /// Default first, then the rest by code. Discovery order is file-system order, which would have
    /// silently reshuffled the language picker the moment the single table was split into files —
    /// alphabetically en-US.lang.xml precedes zh-CN.lang.xml, so the default would no longer have
    /// led the list.
    /// </summary>
    private void SortAvailableLanguages()
    {
        var ordered = _availableLanguages
            .OrderByDescending(option => string.Equals(option.Code, _defaultLanguageCode, StringComparison.OrdinalIgnoreCase))
            .ThenBy(option => option.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _availableLanguages.Clear();
        _availableLanguages.AddRange(ordered);
    }

    /// <summary>
    /// Keys present in some language but not others.
    /// </summary>
    /// <remarks>
    /// This exists BECAUSE of the split. While every language lived in one file, a missing key was
    /// findable by eye and by a one-line grep; once each language is its own document, a gap is
    /// invisible — <see cref="TryGetText"/> quietly falls back to the default language and the
    /// screen looks fine in testing. So the gap is computed and surfaced instead of discovered by a
    /// customer reading half-translated UI.
    ///
    /// Reported, not thrown. A translation gap is a defect to fix, not a reason to refuse to start
    /// in front of a user — the fallback already renders something readable. The test harness is
    /// what turns this list into a failure.
    /// </remarks>
    private IReadOnlyList<LanguageKeyGap> FindKeyGaps()
    {
        var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in _translations.Values)
            allKeys.UnionWith(map.Keys);

        return _availableLanguages
            .Select(option => new LanguageKeyGap(
                option.Code,
                allKeys.Where(key => !_translations[option.Code].ContainsKey(key))
                       .OrderBy(key => key, StringComparer.Ordinal)
                       .ToList()))
            .Where(gap => gap.MissingKeys.Count > 0)
            .ToList();
    }

    private static void ReportKeyGaps(IEnumerable<LanguageKeyGap> gaps)
    {
        foreach (var gap in gaps)
        {
            Trace.TraceWarning(
                $"[localization] {gap.LanguageCode} is missing {gap.MissingKeys.Count} key(s): " +
                string.Join(", ", gap.MissingKeys.Take(10)) +
                (gap.MissingKeys.Count > 10 ? ", …" : string.Empty));
        }
    }

    private readonly record struct LanguageBlock(XElement Node, string Origin);

    public bool SetLanguage(string code)
    {
        if (!_translations.ContainsKey(code))
            return false;

        if (string.Equals(_currentLanguageCode, code, StringComparison.OrdinalIgnoreCase))
            return true;

        _currentLanguageCode = code;
        NotifyLanguageChanged();
        return true;
    }

    public string Format(string key, params object[] args)
    {
        var template = this[key];
        return string.Format(template, args);
    }

    /// <summary>
    /// Joins items into a prose list the way the CURRENT language punctuates one — "Jacket, Shirt"
    /// in English, and the same pair joined with an ideographic comma in Chinese and Japanese.
    /// </summary>
    /// <remarks>
    /// Exposed as a join rather than as a raw separator property deliberately. Every call site used
    /// to hold its own copy of a `code.StartsWith("zh")` ternary choosing the separator — four of
    /// them, one of which carried a comment explaining that it had to be kept in step with another.
    /// separator invites that back; handing out the join does not.
    /// </remarks>
    public string JoinList(IEnumerable<string> values)
        => string.Join(this[ListSeparatorKey], values);

    /// <summary>
    /// As <see cref="JoinList(IEnumerable{string})"/>, but punctuated for a NAMED language rather
    /// than the current one — for documents rendered in a language the UI is not currently in.
    /// </summary>
    public string JoinList(IEnumerable<string> values, string languageCode)
        => string.Join(GetText(ListSeparatorKey, languageCode), values);

    /// <summary>
    /// Joins short metadata fragments onto one line — "CAD  ·  3 orders". A different rule from
    /// <see cref="JoinList(IEnumerable{string})"/>: that one builds prose, this one builds a
    /// summary strip, and a language may well want to punctuate the two differently.
    /// </summary>
    public string JoinFragments(IEnumerable<string> values)
        => string.Join(this[BulletSeparatorKey], values);

    private bool TryGetText(string key, out string value)
    {
        if (_translations.TryGetValue(_currentLanguageCode, out var current)
            && current.TryGetValue(key, out var currentValue)
            && currentValue is not null)
        {
            value = currentValue;
            return true;
        }

        if (_translations.TryGetValue(_defaultLanguageCode, out var fallback)
            && fallback.TryGetValue(key, out var fallbackValue)
            && fallbackValue is not null)
        {
            value = fallbackValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(CurrentLanguageCode));
        OnPropertyChanged("Item[]");
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record LanguageOption(string Code, string Name);

/// <summary>One language's missing keys, relative to the union of every shipped language.</summary>
public sealed record LanguageKeyGap(string LanguageCode, IReadOnlyList<string> MissingKeys);

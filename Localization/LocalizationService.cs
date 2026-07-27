using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace CameywareOrder.Localization;

public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    private readonly Dictionary<string, Dictionary<string, string>> _translations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LanguageOption> _availableLanguages = new();

    private string _defaultLanguageCode = "en-US";
    private string _currentLanguageCode = "en-US";

    private LocalizationService() { }

    public IReadOnlyList<LanguageOption> AvailableLanguages => _availableLanguages;

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

    public void LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Language file not found: {filePath}", filePath);

        var doc = XDocument.Load(filePath);
        var root = doc.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "Languages", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid language XML format: missing <Languages> root.");

        _translations.Clear();
        _availableLanguages.Clear();

        foreach (var langNode in root.Elements("Language"))
        {
            var code = (string?)langNode.Attribute("code");
            var name = (string?)langNode.Attribute("name");

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
                continue;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var textNode in langNode.Elements("Text"))
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
            throw new InvalidDataException("No languages found in language XML file.");

        var xmlDefault = (string?)root.Attribute("default");
        _defaultLanguageCode = !string.IsNullOrWhiteSpace(xmlDefault) && _translations.ContainsKey(xmlDefault)
            ? xmlDefault
            : _availableLanguages[0].Code;

        _currentLanguageCode = _defaultLanguageCode;
        NotifyLanguageChanged();
    }

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

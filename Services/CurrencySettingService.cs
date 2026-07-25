using System.ComponentModel;
using System.IO;
using System.Text.Json;
using LeeYongeOrdering.Models;
using Path = System.IO.Path;

namespace LeeYongeOrdering.Services;

/// <summary>
/// Application-wide currency setting. For a small business the currency is a
/// single global choice rather than a per-order field, so this singleton owns the
/// selected <see cref="CurrencyType"/>, exposes its display symbol, and persists
/// the choice as JSON under the app's local AppData folder. Mirrors the resilient,
/// non-fatal persistence style used by the language preference store.
/// </summary>
public sealed class CurrencySettingService : INotifyPropertyChanged
{
    public static CurrencySettingService Instance { get; } = new();

    private const string FileName = "currency-setting.json";

    private CurrencyType _current = CurrencyType.CAD;

    private CurrencySettingService()
    {
        _current = TryLoad() ?? CurrencyType.CAD;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CurrencyChanged;

    public CurrencyType Current => _current;

    /// <summary>Display symbol for the current currency (￥ for CNY, otherwise $).</summary>
    public string Symbol => GetSymbol(_current);

    public static string GetSymbol(CurrencyType currencyType)
        => currencyType == CurrencyType.CNY ? "￥" : "$";

    public void SetCurrency(CurrencyType currencyType)
    {
        if (_current == currencyType)
            return;

        _current = currencyType;
        Save(currencyType);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Symbol)));
        CurrencyChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string SettingDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LeeYongeOrdering");

    private static string SettingFilePath => Path.Combine(SettingDirectory, FileName);

    private static CurrencyType? TryLoad()
    {
        try
        {
            if (!File.Exists(SettingFilePath))
                return null;

            var json = File.ReadAllText(SettingFilePath);
            var payload = JsonSerializer.Deserialize<CurrencySettingPayload>(json);
            return payload?.Currency;
        }
        catch
        {
            return null;
        }
    }

    private static void Save(CurrencyType currencyType)
    {
        try
        {
            Directory.CreateDirectory(SettingDirectory);
            var payload = new CurrencySettingPayload { Currency = currencyType };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingFilePath, json);
        }
        catch
        {
            // Non-fatal: the app keeps working with the in-memory value even if persistence fails.
        }
    }

    private sealed class CurrencySettingPayload
    {
        public CurrencyType Currency { get; set; } = CurrencyType.CAD;
    }
}

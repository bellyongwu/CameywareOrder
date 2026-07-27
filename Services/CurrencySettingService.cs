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

    // Value from the legacy per-machine JSON file. Since currency became a per-shop setting this
    // is only the pre-shop fallback and the seed the first shop was migrated from; once a shop is
    // bound, the shop row is the source of truth.
    private CurrencyType _legacy = CurrencyType.CAD;
    private Shop? _shop;

    private CurrencySettingService()
    {
        _legacy = TryLoad() ?? CurrencyType.CAD;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CurrencyChanged;

    public CurrencyType Current => _shop?.CurrencyType ?? _legacy;

    /// <summary>
    /// Points the service at a shop, making that shop's row the store. Called when a shop is opened
    /// and again on every shop switch, so the symbol follows the shop without recreating anything.
    /// </summary>
    public void BindTo(Shop shop)
    {
        ArgumentNullException.ThrowIfNull(shop);

        _shop = shop;
        RaiseChanged();
    }

    /// <summary>Display symbol for the current currency (￥ for CNY, otherwise $).</summary>
    public string Symbol => GetSymbol(Current);

    public static string GetSymbol(CurrencyType currencyType)
        => currencyType == CurrencyType.CNY ? "￥" : "$";

    public void SetCurrency(CurrencyType currencyType)
    {
        if (Current == currencyType)
            return;

        if (_shop is null)
        {
            // No shop open yet (only reachable before startup finishes): keep writing the legacy
            // file so the value is still there to migrate from.
            _legacy = currencyType;
            Save(currencyType);
        }
        else
        {
            ShopContext.Instance.UpdateActiveShop(shop => shop.CurrencyType = currencyType);
        }

        RaiseChanged();
    }

    private void RaiseChanged()
    {
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

using System.ComponentModel;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CameywareOrder.Services;

/// <summary>
/// The shop the application is currently working in. Every shop-scoped decision — which orders are
/// listed, which measurement terms and branding are loaded, which currency is shown — resolves
/// through here, so there is exactly one answer to "which shop are we in" at any moment.
///
/// Nothing else may cache a shop id. The GraphQL resolvers read this from Kestrel threads while
/// the UI thread can swap shops, so <see cref="SetActive"/> assigns a whole new object to a single
/// field rather than mutating the existing one: a reader sees either the old shop or the new one,
/// never a half-updated mixture.
/// </summary>
public sealed class ShopContext : INotifyPropertyChanged
{
    public static ShopContext Instance { get; } = new();

    private IServiceScopeFactory? _scopeFactory;
    private Shop? _current;

    private ShopContext()
    {
        // The display name is language-dependent, so a language switch has to re-raise it for any
        // binding showing the shop name.
        LocalizationService.Instance.LanguageChanged += (_, _) => Notify(nameof(CurrentName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the active shop has been swapped, for callers that must reload state.</summary>
    public event EventHandler? ShopChanged;

    /// <summary>The active shop, or null before one has been opened.</summary>
    public Shop? Current => _current;

    public bool HasShop => _current is not null;

    /// <summary>
    /// The active shop, or a thrown exception. Deliberately not a silent default: an order written
    /// against shop id 0 would disappear from every view with no error, which is far worse than a
    /// loud failure at the point the mistake is made.
    /// </summary>
    public Shop RequireCurrent()
        => _current ?? throw new InvalidOperationException(
            "No shop is open. A shop must be selected before shop-scoped data is read or written.");

    public int CurrentShopId => RequireCurrent().Id;

    /// <summary>
    /// Display name of the active shop in the current UI language, falling back to the built-in
    /// Main.HeaderTitle string when no shop is open or it has no name. That keeps the header and
    /// the printed receipt from ever rendering blank, and keeps the existing string table entry
    /// meaningful as the default shop name.
    /// </summary>
    public string CurrentName
    {
        get
        {
            var resolved = _current?.ResolveName(LocalizationService.Instance.CurrentLanguageCode);
            return string.IsNullOrWhiteSpace(resolved)
                ? LocalizationService.Instance["Main.HeaderTitle"]
                : resolved;
        }
    }

    /// <summary>
    /// Supplies the scope factory used to persist edits to the active shop. Called once during
    /// startup, after the host is built.
    /// </summary>
    public void Initialize(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    /// <summary>Opens a shop. Pass a detached instance; it becomes the single shared active shop.</summary>
    public void SetActive(Shop shop)
    {
        ArgumentNullException.ThrowIfNull(shop);

        _current = shop;

        Notify(nameof(Current));
        Notify(nameof(HasShop));
        Notify(nameof(CurrentName));
        ShopChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Mutates the active shop and writes it back. Synchronous by design — it updates a single row
    /// in response to a user action, and the callers are UI event handlers.
    /// </summary>
    public void UpdateActiveShop(Action<Shop> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        var shop = RequireCurrent();
        mutate(shop);

        if (_scopeFactory is not null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Shops.Update(shop);
            db.SaveChanges();
        }

        Notify(nameof(Current));
        Notify(nameof(CurrentName));
    }

    private void Notify(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

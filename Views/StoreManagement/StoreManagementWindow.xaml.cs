using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CameywareOrder.Views;

/// <summary>
/// Administrator-only shop administration: take a branch out of service, put it back, download its data,
/// restore it from a file, delete it, or reinitialise the installation.
/// </summary>
/// <remarks>
/// Reached from the Select Shop footer, which is the one screen where "which shops exist" is already the
/// subject. Multi-select is <c>Extended</c>, so ctrl+click adds one and shift+click takes a run, and every
/// action reads the whole selection — the ask was for single, several or all.
///
/// The reversible actions (delist / put back) and the destructive ones (delete / reinitialise) are in
/// separate cards on purpose, and only the second group goes through
/// <see cref="ConfirmDestructiveWindow"/>. Putting them together would make taking a branch out of service
/// feel like the same class of act as deleting it, and the usual result of that is people reaching for
/// delete because it is the button they recognise.
///
/// This window performs nothing itself: `ShopAdministration` owns the rules and `ShopArchive` owns the
/// file format. What lives here is the selection, the wording, and the order the steps happen in.
/// </remarks>
public partial class StoreManagementWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ObservableCollection<StoreRow> _rows = new();

    public StoreManagementWindow(LocalizationService localization, IServiceScopeFactory scopeFactory)
    {
        InitializeComponent();

        _localization = localization;
        _scopeFactory = scopeFactory;

        StoreList.ItemsSource = _rows;
        LoadStores();
    }

    /// <summary>
    /// True once anything happened that the caller's own shop list cannot still be trusted after — a
    /// deletion, a restore, a delisting. The picker reloads on that rather than on "was this window
    /// opened", so cancelling out of everything costs no refresh.
    /// </summary>
    public bool ShopsChanged { get; private set; }

    // ── list ──────────────────────────────────────────────────────────────────────────────────────

    private void LoadStores()
    {
        var shops = ShopAdministration.AllShops(_scopeFactory);

        _rows.Clear();
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var shop in shops)
                _rows.Add(BuildRow(shop, ShopAdministration.CountOrders(db, shop.Id)));
        }

        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshActionState();
    }

    private StoreRow BuildRow(Shop shop, int orderCount)
    {
        var name = shop.ResolveName(_localization.CurrentLanguageCode);
        // JoinFragments takes a sequence and separates with the localized bullet, the same way the shop
        // picker's own detail strip is built — so the two screens punctuate identically per language.
        var detail = _localization.JoinFragments(new[]
        {
            _localization.Format("Store.Manage.OrderCount", orderCount),
            ShopCurrencies.Name(shop.CurrencyType, _localization),
            TaxJurisdictions.For(shop).DisplayName(_localization),
        });

        return new StoreRow(
            shop,
            name,
            detail,
            UserPresentation.AvatarBrush(name),
            UserPresentation.Initial(name),
            // The date only when there is one: a shop delisted before the stamp existed is still
            // delisted, and a badge reading "Delisted on " with nothing after it looks like a fault.
            DelistedBadge(shop),
            shop.IsDelisted ? Visibility.Visible : Visibility.Collapsed,
            orderCount);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "False positive: StoreList is an x:Name instance field from the XAML-generated " +
                        "partial, which SonarLint's single-file pass cannot see. Six call sites read the " +
                        "selection, so inlining it would copy the cast six times.")]
    private List<StoreRow> Selected() => StoreList.SelectedItems.Cast<StoreRow>().ToList();

    private string DelistedBadge(Shop shop)
    {
        if (!shop.IsDelisted)
            return string.Empty;

        return shop.DelistedOnUtc is null
            ? _localization["Store.Manage.Delisted.Badge"]
            : _localization.Format("Store.Manage.DelistedOn",
                shop.DelistedOnUtc.Value.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.CurrentCulture));
    }

    private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => RefreshActionState();

    /// <summary>
    /// Which buttons apply to the current selection. Delist and Activate are enabled only when the
    /// selection actually contains something for them to do, so a selection of already-delisted shops
    /// cannot be "delisted" again into a no-op the user reads as a failure.
    /// </summary>
    private void RefreshActionState()
    {
        var selected = Selected();

        SelectionSummary.Text = selected.Count == 0
            ? _localization["Store.Manage.NothingSelected"]
            : _localization.Format("Store.Manage.SelectedCount", selected.Count,
                selected.Sum(row => row.OrderCount));

        DelistButton.IsEnabled = selected.Exists(row => !row.Shop.IsDelisted);
        ActivateButton.IsEnabled = selected.Exists(row => row.Shop.IsDelisted);
        DownloadButton.IsEnabled = selected.Count > 0;
        DeleteButton.IsEnabled = selected.Count > 0;
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e) => StoreList.SelectAll();

    private void OnClearSelectionClick(object sender, RoutedEventArgs e) => StoreList.UnselectAll();

    // ── reversible ────────────────────────────────────────────────────────────────────────────────

    private void OnDelistClick(object sender, RoutedEventArgs e)
        => ApplyServiceState(delist: true, "Store.Manage.Delisted");

    private void OnActivateClick(object sender, RoutedEventArgs e)
        => ApplyServiceState(delist: false, "Store.Manage.Activated");

    /// <summary>
    /// Delisting or reinstating every shop in the selection that is not already in that state.
    /// </summary>
    /// <remarks>
    /// Re-reads each shop through this scope: the rows hold `AsNoTracking` copies, and writing through an
    /// untracked instance saves nothing at all — silently, which is the worst way for it to fail.
    /// </remarks>
    private void ApplyServiceState(bool delist, string messageKey)
    {
        var wanted = Selected()
            .Where(row => row.Shop.IsDelisted != delist)
            .Select(row => row.Shop.Id)
            .ToList();

        if (wanted.Count == 0)
            return;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var shop in db.Shops.Where(shop => wanted.Contains(shop.Id)).ToList())
            {
                if (delist)
                    ShopAdministration.Delist(db, shop);
                else
                    ShopAdministration.Activate(db, shop);
            }
        }

        ShopsChanged = true;
        LoadStores();
        ReportSuccess(_localization.Format(messageKey, wanted.Count));
    }

    // ── data ──────────────────────────────────────────────────────────────────────────────────────

    private void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0)
            return;

        if (!TryPickSaveTarget(selected.Count, out var path))
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var summary = ShopArchive.Export(db, selected.Select(row => row.Shop).ToList(), path);

            ReportSuccess(_localization.Format("Store.Manage.Downloaded", summary.Shops, summary.Orders, path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportFailure(_localization.Format("Store.Manage.Failed", ex.Message));
        }
    }

    /// <summary>
    /// Restores from a file the administrator picks. The archive is READ first and its contents reported,
    /// so a mistyped file name or a zip that is not an archive is refused before anything is written.
    /// </summary>
    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = _localization["Store.Manage.FileFilter"],
            Title = _localization["Store.Manage.Restore"],
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        var summary = ShopArchive.TryRead(dialog.FileName);
        if (summary is null)
        {
            ReportFailure(_localization["Store.Manage.NotAnArchive"]);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var result = ShopArchive.Import(db, dialog.FileName);

            ShopsChanged = true;
            LoadStores();

            // A skipped shop is not a failure and not a success — it is the one outcome the user will
            // otherwise mistake for "the restore did nothing", so it gets said out loud.
            ReportSuccess(result.Skipped > 0
                ? _localization.Format("Store.Manage.RestoredWithSkips", result.Shops, result.Orders, result.Skipped)
                : _localization.Format("Store.Manage.Restored", result.Shops, result.Orders));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException)
        {
            ReportFailure(_localization.Format("Store.Manage.Failed", ex.Message));
        }
    }

    // ── destructive ───────────────────────────────────────────────────────────────────────────────

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0)
            return;

        var impact = selected
            .Select(row => _localization.Format("Store.Manage.ImpactLine", row.Name, row.OrderCount))
            .ToList();

        var confirmed = Confirm(
            _localization.Format("Store.Manage.DeleteHeadline", selected.Count),
            impact,
            _localization["Store.Confirm.RemoveNow"]);

        if (confirmed is null)
            return;

        if (confirmed == ConfirmedAction.SaveThenProceed && !SaveRecordsFirst(selected))
            return;

        DeleteShops(selected);
    }

    /// <summary>
    /// Reinitialise: every shop, every order, every per-shop file. Accounts, the saved language and the
    /// global settings are kept, so nobody is locked out and the next sign-in lands on the
    /// create-first-shop path.
    /// </summary>
    private void OnReinitializeClick(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0)
        {
            ReportFailure(_localization["Store.Manage.NothingToReinitialize"]);
            return;
        }

        var everything = _rows.ToList();
        var impact = new List<string>
        {
            _localization.Format("Store.Manage.ImpactAllStores", everything.Count, everything.Sum(row => row.OrderCount)),
            _localization["Store.Manage.ImpactPerShopFiles"],
            _localization["Store.Manage.ImpactAccountsKept"],
        };

        var confirmed = Confirm(
            _localization["Store.Manage.ReinitializeHeadline"],
            impact,
            _localization["Store.Confirm.ReinitializeNow"]);

        if (confirmed is null)
            return;

        if (confirmed == ConfirmedAction.SaveThenProceed && !SaveRecordsFirst(everything))
            return;

        DeleteShops(everything);
    }

    private ConfirmedAction? Confirm(string headline, IReadOnlyList<string> impact, string proceedLabel)
    {
        var window = new ConfirmDestructiveWindow(_localization, headline, impact, proceedLabel) { Owner = this };
        return window.ShowDialog() is true ? window.Action : null;
    }

    /// <summary>
    /// The "save the store records" half of the confirmation. Returns false when the export did not
    /// happen — cancelled dialog or a write failure — and the caller then does NOT delete, because the
    /// user asked for the records to be kept and they were not.
    /// </summary>
    private bool SaveRecordsFirst(IReadOnlyList<StoreRow> rows)
    {
        if (!TryPickSaveTarget(rows.Count, out var path))
            return false;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var summary = ShopArchive.Export(db, rows.Select(row => row.Shop).ToList(), path);

            ReportSuccess(_localization.Format("Store.Manage.Downloaded", summary.Shops, summary.Orders, path));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportFailure(_localization.Format("Store.Manage.SaveFailedNoDelete", ex.Message));
            return false;
        }
    }

    private void DeleteShops(IReadOnlyList<StoreRow> rows)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var result = ShopAdministration.Delete(db, rows.Select(row => row.Shop).ToList());

            ShopsChanged = true;
            LoadStores();
            ReportSuccess(_localization.Format("Store.Manage.Deleted", result.Shops, result.Orders));
        }
        catch (Exception ex) when (ex is DbUpdateException or IOException)
        {
            ReportFailure(_localization.Format("Store.Manage.Failed", ex.Message));
        }
    }

    // ── plumbing ──────────────────────────────────────────────────────────────────────────────────

    private bool TryPickSaveTarget(int shopCount, out string path)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = _localization["Store.Manage.FileFilter"],
            Title = _localization["Store.Manage.Download"],
            FileName = $"stores-{shopCount}-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
        };

        // GetValueOrDefault, not `is true`: the bool? is CONSUMED as a bool here, and Sonar flags
        // `is true` in that position (S1125). Behaviourally identical for bool?.
        var picked = dialog.ShowDialog(this).GetValueOrDefault();
        path = picked ? dialog.FileName : string.Empty;
        return picked;
    }

    private void ReportSuccess(string message) => Report(message, Color.FromRgb(0x04, 0x78, 0x57));

    private void ReportFailure(string message) => Report(message, Color.FromRgb(0xB9, 0x1C, 0x1C));

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "False positive: StatusText is an x:Name instance field from the XAML-generated " +
                        "partial, which SonarLint's single-file pass cannot see.")]
    private void Report(string message, Color colour)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(colour);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>One shop on screen. Holds the shop so an action can act on it without a second lookup.</summary>
    private sealed record StoreRow(
        Shop Shop,
        string Name,
        string Detail,
        Brush AvatarBrush,
        string Initial,
        string DelistedText,
        Visibility DelistedVisibility,
        int OrderCount);
}

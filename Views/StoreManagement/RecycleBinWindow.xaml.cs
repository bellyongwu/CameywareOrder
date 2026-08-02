using System.Collections.ObjectModel;
using System.Globalization;
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
/// The open shop's recycle bin: what has been deleted, how long each has left, and the two things
/// that can be done about it.
/// </summary>
/// <remarks>
/// Shaped after <see cref="StoreManagementWindow"/> deliberately — the same card list, the same
/// selection summary, the same split between a reversible action and a destructive one in its own
/// red card. Two screens that do the same KIND of thing should not have to be learned twice.
///
/// It performs nothing itself: <c>OrderRecycleBin</c> owns every rule, including which rows a
/// restore may reach. What lives here is the selection, the wording, and the confirmation in front of
/// the irreversible half.
/// </remarks>
public partial class RecycleBinWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ObservableCollection<BinRow> _rows = new();

    public RecycleBinWindow(LocalizationService localization, IServiceScopeFactory scopeFactory)
    {
        InitializeComponent();

        _localization = localization;
        _scopeFactory = scopeFactory;

        BinList.ItemsSource = _rows;

        // The retention window is a number this installation chose, so the subtitle has to be
        // composed rather than bound — and it says something different when nothing is purged at all,
        // because "kept for 0 days" would read as "deleted immediately", the opposite of the truth.
        var days = DataProtectionStore.Instance.Settings.RecycleBinDays;
        SubtitleText.Text = days > 0
            ? _localization.Format("RecycleBin.Subtitle", days)
            : _localization["RecycleBin.SubtitleForever"];

        LoadBin();
    }

    /// <summary>
    /// True once anything was restored or destroyed, so the caller knows its order list is stale.
    /// Reported rather than acted on: this window has no opinion about what the main list shows.
    /// </summary>
    public bool OrdersChanged { get; private set; }

    // ── list ──────────────────────────────────────────────────────────────────────────────────────

    private void LoadBin()
    {
        var settings = DataProtectionStore.Instance.Settings;

        _rows.Clear();
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var order in OrderRecycleBin.List(db))
                _rows.Add(BuildRow(order, settings));
        }

        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyBinButton.IsEnabled = _rows.Count > 0;
        RefreshActionState();
    }

    private BinRow BuildRow(Order order, DataProtectionSettings settings)
    {
        var headline = _localization.Format("RecycleBin.Headline", order.OrderNumber, order.CustomerName);

        // Every fragment is a whole localized SENTENCE, never a label with a value pasted after it:
        // a language that writes the date first, or that puts no space between them, cannot be
        // produced by concatenating in C#. JoinFragments then punctuates between them per language.
        var detail = _localization.JoinFragments(new[]
        {
            _localization.Format("RecycleBin.DeletedOn", Day(order.DeletedOnLocal)),
            _localization.Format("RecycleBin.OrderedOn", Day(order.OrderDateLocal)),
            ShopCurrencies.SymbolOf(order) + order.TotalAmount.ToString("N2", CultureInfo.CurrentCulture),
        });

        return new BinRow(order.Id, headline, detail, DescribeRemaining(order, settings),
            BadgeBackgroundFor(order, settings), BadgeForegroundFor(order, settings));
    }

    private static string Day(DateTime? moment)
        => moment?.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture) ?? string.Empty;

    // Named Describe.../...For rather than after the row properties they fill: a private method and a
    // nested record's property sharing a name is S3218, and the BOUND names are the ones that must
    // not move — a renamed binding fails silently at runtime.
    /// <summary>How long this order has left, or that nothing is going to take it.</summary>
    private string DescribeRemaining(Order order, DataProtectionSettings settings)
    {
        if (!settings.PurgesAutomatically)
            return _localization["RecycleBin.KeptForever"];

        var days = DaysLeft(order, settings);

        // Zero rather than a negative number: an order past its window is one the next launch will
        // purge, and "-3 days left" is not something to put in front of anybody.
        return days <= 0
            ? _localization["RecycleBin.PurgeNext"]
            : _localization.Format("RecycleBin.DaysLeft", days);
    }

    private static int DaysLeft(Order order, DataProtectionSettings settings)
    {
        if (order.DeletedOnUtc is not { } deleted)
            return settings.RecycleBinDays;

        var elapsed = (DateTime.UtcNow - deleted).TotalDays;
        return (int)Math.Ceiling(settings.RecycleBinDays - elapsed);
    }

    // Amber while there is time, red once the next purge takes it — the same pair the pickup queue
    // uses for "due soon" and "overdue", so the meaning carries across screens.
    private static Brush BadgeBackgroundFor(Order order, DataProtectionSettings settings)
        => IsExpiring(order, settings) ? Frozen("#FEE2E2") : Frozen("#FEF3C7");

    private static Brush BadgeForegroundFor(Order order, DataProtectionSettings settings)
        => IsExpiring(order, settings) ? Frozen("#B91C1C") : Frozen("#92400E");

    private static bool IsExpiring(Order order, DataProtectionSettings settings)
        => settings.PurgesAutomatically && DaysLeft(order, settings) <= 3;

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private List<BinRow> Selected() => BinList.SelectedItems.Cast<BinRow>().ToList();

    private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => RefreshActionState();

    private void RefreshActionState()
    {
        var count = BinList.SelectedItems.Count;

        SelectionSummary.Text = count == 0
            ? _localization.Format("RecycleBin.Holding", _rows.Count)
            : _localization.Format("RecycleBin.SelectedCount", count);

        RestoreButton.IsEnabled = count > 0;
        DeleteForeverButton.IsEnabled = count > 0;
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e) => BinList.SelectAll();

    private void OnClearSelectionClick(object sender, RoutedEventArgs e) => BinList.UnselectAll();

    // ── reversible ────────────────────────────────────────────────────────────────────────────────

    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        var ids = Selected().Select(row => row.OrderId).ToList();
        if (ids.Count == 0 || !AuthenticationService.Instance.CanManageRecycleBin)
            return;

        int restored;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            restored = OrderRecycleBin.Restore(db, ids);
        }

        OrdersChanged = true;
        LoadBin();
        ReportSuccess(_localization.Format("RecycleBin.Restored", restored));
    }

    // ── destructive ───────────────────────────────────────────────────────────────────────────────

    private void OnDeleteForeverClick(object sender, RoutedEventArgs e)
    {
        var selected = Selected();
        if (selected.Count == 0)
            return;

        // The impact lines name the orders rather than counting them. This is the ONE action in the
        // application after which a record cannot be recovered by any means, so the confirmation
        // shows what is about to go.
        var impact = selected.Take(12).Select(row => row.Headline).ToList();
        if (selected.Count > impact.Count)
            impact.Add(_localization.Format("RecycleBin.AndMore", selected.Count - impact.Count));

        Purge(
            _localization.Format("RecycleBin.DeleteForeverHeadline", selected.Count),
            impact,
            selected.Select(row => row.OrderId).ToList());
    }

    private void OnEmptyBinClick(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0)
            return;

        Purge(
            _localization.Format("RecycleBin.EmptyBinHeadline", _rows.Count),
            new[] { _localization.Format("RecycleBin.EmptyBinImpact", _rows.Count) },
            _rows.Select(row => row.OrderId).ToList());
    }

    /// <summary>
    /// The typed-phrase confirmation and the purge behind it — one path for both destructive
    /// buttons, so they cannot end up guarded differently.
    /// </summary>
    private void Purge(string headline, IReadOnlyList<string> impact, IReadOnlyList<int> orderIds)
    {
        if (!AuthenticationService.Instance.CanManageRecycleBin)
            return;

        var window = new ConfirmDestructiveWindow(
            _localization, headline, impact, _localization["RecycleBin.Confirm"]) { Owner = this };

        if (window.ShowDialog() is not true)
            return;

        // SaveThenProceed is not offered here and would mean nothing if it were: these records are
        // already deleted, and the safety copy the shop wants is the scheduled backup, not an export
        // of a bin. The dialog's other outcome is the only one this path acts on.
        try
        {
            int removed;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                removed = OrderRecycleBin.PurgeForever(db, deletedBeforeUtc: null, orderIds);
            }

            OrdersChanged = true;
            LoadBin();
            ReportSuccess(_localization.Format("RecycleBin.Purged", removed));
        }
        catch (Exception ex) when (ex is DbUpdateException or IOException)
        {
            ReportFailure(_localization.Format("Store.Manage.Failed", ex.Message));
        }
    }

    // ── plumbing ──────────────────────────────────────────────────────────────────────────────────

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

    /// <summary>One deleted order as the card template renders it.</summary>
    /// <remarks>
    /// Holds the ID rather than the order. Every action re-reads from the database anyway — a bin
    /// left open while somebody else works is exactly where a stale entity would be acted on — and an
    /// id is the only part of a row that cannot go out of date.
    /// </remarks>
    private sealed record BinRow(
        int OrderId,
        string Headline,
        string Detail,
        string RemainingText,
        Brush RemainingBackground,
        Brush RemainingForeground);
}

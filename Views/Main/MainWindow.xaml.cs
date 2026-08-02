using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CameywareOrder.Controls;
using CameywareOrder.Converters;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using CameywareOrder.ViewModels;
using CameywareOrder.Views;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;

namespace CameywareOrder;

/// <summary>
/// The order list and everything a shop does with it.
/// </summary>
/// <remarks>
/// Implements <see cref="ICopyPasteSurface"/> so the orders list gets Ctrl+C / Ctrl+V from the shared
/// <see cref="CopyPasteBinding"/> rather than from a keyboard switch of its own. The five members are
/// grouped at the foot of this file.
/// </remarks>
public partial class MainWindow : Window, ICopyPasteSurface
{
    private static readonly string ExplorerPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    // Shared file-dialog filter for the JSON import/export dialogs (measurement terms, branding).
    private const string JsonFileFilter = "JSON (*.json)|*.json";

    private readonly MainViewModel _viewModel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LocalizationService _localization;
    private bool _isLanguageSwitchInitializing;
    private bool _suppressLanguageRefresh;

    /// <summary>
    /// Whether the second filter row is showing. Closed on open: the everyday case is typing a name
    /// into the search box, and four more controls on permanent display are four to read past.
    /// </summary>
    private bool _advancedSearchOpen;

    public MainWindow(
        MainViewModel viewModel,
        IServiceScopeFactory scopeFactory,
        LocalizationService localization)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _scopeFactory = scopeFactory;
        _localization = localization;
        DataContext = _viewModel;

        ApplyRolePermissions();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.SelectionRequested += OnSelectionRequested;
        _localization.LanguageChanged += OnLanguageChangedGlobally;
        ShopContext.Instance.ShopChanged += OnShopChanged;
        RefreshToolbarLabels();
        RefreshAdvancedSearch();

        // The strip reads the database directly rather than the loaded page, so it does not wait on
        // the list — and it is refreshed again whenever the orders change, since a saved order moves
        // the month's figures.
        _viewModel.PropertyChanged += OnViewModelChanged;
        RefreshSummaryStrip();
        _ = _viewModel.LoadOrdersAsync();
    }

    /// <summary>Keeps the month's figures in step with the records underneath them.</summary>
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // FilteredCount moves on every reload, which is the cheapest honest signal that the order
        // set has changed — a save, a delete, a copy, or a shop switch.
        if (e.PropertyName == nameof(MainViewModel.FilteredCount))
            RefreshSummaryStrip();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedOrder))
            RefreshToolbarLabels();

        // So the "a filter is hiding in here" mark appears the moment one is set, including when
        // Clear filters removes the last of them.
        if (e.PropertyName == nameof(MainViewModel.Query))
            RefreshAdvancedSearch();
    }

    /// <summary>
    /// Drops the subscriptions this window and its view model hold on the application singletons.
    /// Signing out builds a NEW MainWindow, so without this each sign-out would leave a dead
    /// window listening for language and shop changes and updating controls nobody can see.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _localization.LanguageChanged -= OnLanguageChangedGlobally;
        ShopContext.Instance.ShopChanged -= OnShopChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.SelectionRequested -= OnSelectionRequested;
        _viewModel.Detach();

        base.OnClosed(e);
    }

    // ── Copy / paste surface (Ctrl+C, Ctrl+V on the orders list) ──────────────────────────────────

    /// <summary>
    /// The clipboard kind orders are held under, scoped to the OPEN SHOP.
    /// </summary>
    /// <remarks>
    /// The shop is part of the token, not just a check inside <see cref="CanPaste"/>. Orders copied
    /// in one branch must not paste into the next: the copy reads its source through a context
    /// confined to the open shop, so a cross-shop paste would find nothing and report copying zero
    /// records — a silent no-op reads as a broken feature. Baking the shop into the kind disables
    /// paste outright the moment the shop changes, which says the same thing before the key is
    /// pressed.
    /// </remarks>
    public string ClipboardKind => $"Orders@{ShopContext.Instance.Current?.PublicId}";

    /// <summary>
    /// The same gate the Copy action carries: a selection AND the capability. The keyboard reaches
    /// this without passing any chrome, so the permission has to be checked here too.
    /// </summary>
    public bool CanCopy => _viewModel.HasSelection && AuthenticationService.Instance.CanCopyOrders;

    public IReadOnlyList<object> CopySelection()
        => _viewModel.SelectedOrders.Select(order => (object)order.Id).ToList();

    public bool CanPaste(IReadOnlyList<object> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Count > 0 && AuthenticationService.Instance.CanCopyOrders;
    }

    public void Paste(IReadOnlyList<object> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (!AuthenticationService.Instance.CanCopyOrders)
            return;

        var ids = items.OfType<int>().Distinct().ToList();
        if (ids.Count == 0)
            return;

        // Not awaited, exactly as CopyOrderCommand is not: the view model reports through
        // StatusMessage and reloads the list itself.
        _ = _viewModel.CopyOrdersAsync(ids);
    }
}

// Attached properties that let each GridViewColumn declare the Order member it sorts by
// (SortKey) and carry the current sort-direction arrow (SortGlyph) shown in its header.
public static class OrderColumnSort
{
    public static readonly DependencyProperty SortKeyProperty =
        DependencyProperty.RegisterAttached(
            "SortKey", typeof(string), typeof(OrderColumnSort), new PropertyMetadata(string.Empty));

    public static void SetSortKey(DependencyObject element, string value) => element.SetValue(SortKeyProperty, value);

    public static string GetSortKey(DependencyObject element) => (string)element.GetValue(SortKeyProperty);

    public static readonly DependencyProperty SortGlyphProperty =
        DependencyProperty.RegisterAttached(
            "SortGlyph", typeof(string), typeof(OrderColumnSort), new PropertyMetadata(string.Empty));

    public static void SetSortGlyph(DependencyObject element, string value) => element.SetValue(SortGlyphProperty, value);

    public static string GetSortGlyph(DependencyObject element) => (string)element.GetValue(SortGlyphProperty);
}

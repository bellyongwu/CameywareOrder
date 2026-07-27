using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;

namespace CameywareOrder.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LocalizationService _localization;
    private List<Order> _allOrders = new();
    private ObservableCollection<Order> _orders = new();
    private Order? _selectedOrder;
    private string _statusMessage;
    private string _searchText = string.Empty;
    private StatusFilterOption _selectedStatusFilter;
    private int _pageSize = 20;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _filteredCount;
    private string? _sortKey;
    private bool _sortAscending = true;

    public MainViewModel(IServiceScopeFactory scopeFactory, LocalizationService localization)
    {
        _scopeFactory = scopeFactory;
        _localization = localization;
        _statusMessage = _localization["Status.Ready"];
        _selectedStatusFilter = StatusFilterOptions[0];

        _localization.LanguageChanged += OnLanguageChanged;

        LoadOrdersCommand = new RelayCommand(async _ => await LoadOrdersAsync());
        NextPageCommand = new RelayCommand(_ => GoToNextPage(), _ => CanGoToNextPage);
        PreviousPageCommand = new RelayCommand(_ => GoToPreviousPage(), _ => CanGoToPreviousPage);
        DeleteOrderCommand = new RelayCommand(
            async _ => await DeleteOrderAsync(),
            _ => SelectedOrder is not null);
        CopyOrderCommand = new RelayCommand(
            async _ => await CopyOrderAsync(),
            _ => SelectedOrder is not null);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        StatusMessage = _localization["Status.Ready"];
        OnPropertyChanged(nameof(PageSummary));
    }

    /// <summary>
    /// Releases the singleton subscriptions this view model holds. Called when its window closes.
    /// Signing out builds a fresh MainWindow and view model, so without this every sign-out would
    /// leave another dead listener on the localization singleton, and a language switch would
    /// update view models nothing is showing.
    /// </summary>
    public void Detach() => _localization.LanguageChanged -= OnLanguageChanged;

    // ── Bindable properties ────────────────────────────────────────────────────

    public ObservableCollection<Order> Orders
    {
        get => _orders;
        set { _orders = value; OnPropertyChanged(); }
    }

    public Order? SelectedOrder
    {
        get => _selectedOrder;
        set { _selectedOrder = value; OnPropertyChanged(); }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
                return;

            _searchText = value;
            OnPropertyChanged();
            _currentPage = 1;
            RebuildOrdersView();
        }
    }

    public IReadOnlyList<StatusFilterOption> StatusFilterOptions { get; } = new StatusFilterOption[]
    {
        new(null),
        new(OrderStatus.Processing),
        new(OrderStatus.Shipped),
        new(OrderStatus.Completed),
        new(OrderStatus.Cancelled),
        new(OrderStatus.Returned)
    };

    public StatusFilterOption SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (value is null || _selectedStatusFilter == value)
                return;

            _selectedStatusFilter = value;
            OnPropertyChanged();
            _currentPage = 1;
            RebuildOrdersView();
        }
    }

    public int PageSize
    {
        get => _pageSize;
        set
        {
            var normalized = value <= 0 ? 20 : value;
            if (_pageSize == normalized)
                return;

            _pageSize = normalized;
            OnPropertyChanged();
            _currentPage = 1;
            RebuildOrdersView();
        }
    }

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (_currentPage == value)
                return;

            _currentPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageSummary));
            OnPropertyChanged(nameof(CanGoToPreviousPage));
            OnPropertyChanged(nameof(CanGoToNextPage));
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        private set
        {
            if (_totalPages == value)
                return;

            _totalPages = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageSummary));
            OnPropertyChanged(nameof(CanGoToPreviousPage));
            OnPropertyChanged(nameof(CanGoToNextPage));
        }
    }

    public IReadOnlyList<int> PageSizeOptions { get; } = new[] { 20, 50, 100 };

    public string PageSummary => _localization.Format("Paging.Summary", CurrentPage, TotalPages, _filteredCount);

    public bool CanGoToPreviousPage => CurrentPage > 1;

    public bool CanGoToNextPage => CurrentPage < TotalPages;

    // Column currently used to sort the orders list, and whether it is ascending.
    // Null means the default load order (newest activity first).
    public string? SortKey => _sortKey;

    public bool SortAscending => _sortAscending;

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Instance property required for WPF data binding ({Binding DatabaseFilePath}).")]
    public string DatabaseFilePath => DatabasePathProvider.DatabaseFilePath;

    // ── Commands ───────────────────────────────────────────────────────────────

    public ICommand LoadOrdersCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand DeleteOrderCommand { get; }
    public ICommand CopyOrderCommand { get; }

    // ── Operations ─────────────────────────────────────────────────────────────

    public async Task LoadOrdersAsync()
    {
        try
        {
            StatusMessage = _localization["Status.LoadingOrders"];
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var orders = await db.Orders
                .Include(o => o.Items)
                .OrderByDescending(o => o.LastModifiedDate ?? o.OrderDate)
                .ToListAsync();

            _allOrders = orders;
            CurrentPage = 1;
            RebuildOrdersView();

            StatusMessage = _localization.Format("Status.LoadedSummary", _allOrders.Count);
        }
        catch (Exception ex)
        {
            // Drop whatever was loaded before. Leaving it in place means a failed reload after a
            // shop switch shows the PREVIOUS shop's orders under the new shop's name, with
            // SelectedOrder pointing at an order that Delete / Copy / Print would then act on.
            // An empty list is recoverable; acting on another shop's order is not.
            _allOrders = new List<Order>();
            SelectedOrder = null;
            CurrentPage = 1;
            RebuildOrdersView();

            StatusMessage = _localization.Format("Status.LoadFailed", ex.Message);
        }
    }

    private void GoToPreviousPage()
    {
        if (!CanGoToPreviousPage)
            return;

        CurrentPage--;
        RebuildOrdersView();
    }

    private void GoToNextPage()
    {
        if (!CanGoToNextPage)
            return;

        CurrentPage++;
        RebuildOrdersView();
    }

    // Toggles the sort direction when the same column header is clicked again, or
    // switches to the newly clicked column (ascending first). Sorting applies to the
    // whole filtered set before paging, then resets to the first page.
    public void SortBy(string sortKey)
    {
        if (string.IsNullOrWhiteSpace(sortKey))
            return;

        if (_sortKey == sortKey)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortKey = sortKey;
            _sortAscending = true;
        }

        OnPropertyChanged(nameof(SortKey));
        OnPropertyChanged(nameof(SortAscending));
        _currentPage = 1;
        RebuildOrdersView();
    }

    private static Func<Order, object?>? GetSortSelector(string sortKey) => sortKey switch
    {
        nameof(Order.OrderNumber) => order => order.OrderNumber ?? string.Empty,
        nameof(Order.CustomerName) => order => order.CustomerName ?? string.Empty,
        nameof(Order.PhoneNumber) => order => order.PhoneNumber ?? string.Empty,
        nameof(Order.OrderDate) => order => order.OrderDate,
        nameof(Order.Status) => order => (int)order.Status,
        nameof(Order.TotalAmount) => order => order.TotalAmount,
        "BalanceStatus" => order => order.IsBalanceCleared,
        nameof(Order.LastModifiedDate) => order => order.LastModifiedDate ?? order.OrderDate,
        _ => null
    };

    private void RebuildOrdersView()
    {
        var query = _allOrders.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var keyword = SearchText.Trim();
            query = query.Where(order =>
                order.CustomerName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || order.PhoneNumber.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedStatusFilter?.Value is { } status)
        {
            query = query.Where(order => order.Status == status);
        }

        var filtered = query.ToList();
        _filteredCount = filtered.Count;

        // Apply the active column sort across the whole filtered set before paging so
        // sorting spans every page, not just the visible one.
        if (_sortKey is not null && GetSortSelector(_sortKey) is { } selector)
        {
            filtered = _sortAscending
                ? filtered.OrderBy(selector).ToList()
                : filtered.OrderByDescending(selector).ToList();
        }

        TotalPages = Math.Max(1, (int)Math.Ceiling(_filteredCount / (double)PageSize));
        if (CurrentPage > TotalPages)
            CurrentPage = TotalPages;

        var pageItems = filtered
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        Orders.Clear();
        foreach (var order in pageItems)
            Orders.Add(order);

        // Re-point the selection to the freshly loaded instance so the detail panel
        // (balance status, etc.) reflects the latest saved values after an edit.
        if (SelectedOrder is not null)
            SelectedOrder = Orders.FirstOrDefault(order => order.Id == SelectedOrder.Id) ?? Orders.FirstOrDefault();
        else
            SelectedOrder = Orders.FirstOrDefault();

        OnPropertyChanged(nameof(PageSummary));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
    }

    private async Task DeleteOrderAsync()
    {
        if (SelectedOrder is null) return;

        var result = System.Windows.MessageBox.Show(
            _localization.Format("Delete.ConfirmMessage", SelectedOrder.OrderNumber),
            _localization["Delete.ConfirmTitle"],
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // A query, not FindAsync: Find is a key lookup and bypasses the shop query filter, so
            // a stale selection left over from a shop switch could delete another shop's order.
            var orderId = SelectedOrder.Id;
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order is not null)
            {
                db.Orders.Remove(order);
                await db.SaveChangesAsync();
            }
            await LoadOrdersAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("Status.DeleteFailed", ex.Message);
        }
    }

    // Statuses that represent a finished order (Shipped is now also read-only/finalized,
    // same as Completed/Cancelled/Returned). Copying such an order starts a fresh
    // active order, so its status is reset to Processing (which also removes the
    // "picked up" tick, since that flag is derived from the Completed status).
    private static bool IsClosedStatus(OrderStatus status)
        => status is OrderStatus.Shipped or OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.Returned;

    private async Task CopyOrderAsync()
    {
        if (SelectedOrder is null) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var source = await db.Orders
                .Include(o => o.Items)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == SelectedOrder.Id);

            if (source is null)
                return;

            var copy = new Order
            {
                OrderNumber = $"ORD-{DateTime.Now:yyyyMMdd-HHmmss}",
                OrderDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow,
                CustomerName = source.CustomerName,
                PhoneNumber = source.PhoneNumber,
                Email = source.Email,
                Address = source.Address,
                CurrencyType = source.CurrencyType,
                ServiceType = source.ServiceType,
                ServiceDetails = source.ServiceDetails,
                AdditionalNotes = source.AdditionalNotes,
                Subtotal = source.Subtotal,
                TaxRate = source.TaxRate,
                ChestSize = source.ChestSize,
                JacketLength = source.JacketLength,
                CustomMadeRecordsJson = source.CustomMadeRecordsJson,
                // A closed order becomes a new Processing order; otherwise keep its status.
                Status = IsClosedStatus(source.Status) ? OrderStatus.Processing : source.Status,
                TotalAmount = source.TotalAmount,
                Downpayment = source.Downpayment,
                DownpaymentMethod = source.DownpaymentMethod,
                FinalBalanceMethod = source.FinalBalanceMethod,
                AlterationDownpayment = source.AlterationDownpayment,
                AlterationDownpaymentMethod = source.AlterationDownpaymentMethod,
                AlterationDownpaymentCompleted = source.AlterationDownpaymentCompleted,
                AlterationFinalBalanceMethod = source.AlterationFinalBalanceMethod,
                AlterationBalanceCleared = source.AlterationBalanceCleared,
                CustomMadeDownpayment = source.CustomMadeDownpayment,
                CustomMadeDownpaymentMethod = source.CustomMadeDownpaymentMethod,
                CustomMadeDownpaymentCompleted = source.CustomMadeDownpaymentCompleted,
                CustomMadeFinalBalanceMethod = source.CustomMadeFinalBalanceMethod,
                CustomMadeBalanceCleared = source.CustomMadeBalanceCleared,
                ClothingDownpayment = source.ClothingDownpayment,
                ClothingDownpaymentMethod = source.ClothingDownpaymentMethod,
                ClothingDownpaymentCompleted = source.ClothingDownpaymentCompleted,
                ClothingFinalBalanceMethod = source.ClothingFinalBalanceMethod,
                ClothingBalanceCleared = source.ClothingBalanceCleared,
                AlterationSubtotal = source.AlterationSubtotal,
                AlterationTaxRate = source.AlterationTaxRate,
                ClothingSubtotal = source.ClothingSubtotal,
                ClothingTaxRate = source.ClothingTaxRate,
                CustomMadeTaxRate = source.CustomMadeTaxRate,
                Notes = source.Notes,
                Items = source.Items
                    .Select(item => new OrderItem
                    {
                        ProductName = item.ProductName,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        PromotionalPrice = item.PromotionalPrice
                    })
                    .ToList()
            };

            db.Orders.Add(copy);
            await db.SaveChangesAsync();
            await LoadOrdersAsync();

            SelectedOrder = Orders.FirstOrDefault(order => order.Id == copy.Id) ?? SelectedOrder;
            StatusMessage = _localization.Format("Status.CopySucceeded", copy.OrderNumber);
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("Status.CopyFailed", ex.Message);
        }
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record StatusFilterOption(OrderStatus? Value);

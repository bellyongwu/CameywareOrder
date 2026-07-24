using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LeeYongeOrdering.Data;
using LeeYongeOrdering.Localization;
using LeeYongeOrdering.Models;

namespace LeeYongeOrdering.ViewModels;

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

    public MainViewModel(IServiceScopeFactory scopeFactory, LocalizationService localization)
    {
        _scopeFactory = scopeFactory;
        _localization = localization;
        _statusMessage = _localization["Status.Ready"];
        _selectedStatusFilter = StatusFilterOptions[0];

        _localization.LanguageChanged += (_, _) =>
        {
            StatusMessage = _localization["Status.Ready"];
            OnPropertyChanged(nameof(PageSummary));
        };

        LoadOrdersCommand = new RelayCommand(async _ => await LoadOrdersAsync());
        NextPageCommand = new RelayCommand(_ => GoToNextPage(), _ => CanGoToNextPage);
        PreviousPageCommand = new RelayCommand(_ => GoToPreviousPage(), _ => CanGoToPreviousPage);
        DeleteOrderCommand = new RelayCommand(
            async _ => await DeleteOrderAsync(),
            _ => SelectedOrder is not null);
    }

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
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            _allOrders = orders;
            CurrentPage = 1;
            RebuildOrdersView();

            StatusMessage = _localization.Format("Status.LoadedSummary", _allOrders.Count);
        }
        catch (Exception ex)
        {
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
            var order = await db.Orders.FindAsync(SelectedOrder.Id);
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

    // ── INotifyPropertyChanged ─────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record StatusFilterOption(OrderStatus? Value);

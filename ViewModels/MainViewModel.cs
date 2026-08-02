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
using CameywareOrder.Services;

namespace CameywareOrder.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LocalizationService _localization;
    private List<Order> _allOrders = new();
    private ObservableCollection<Order> _orders = new();
    private Order? _selectedOrder;
    private List<Order> _selectedOrders = new();
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
        // Both act on the whole SELECTION, not on the anchor row alone. One command per action
        // whatever the count, so the action bar, the row context menu and the Delete key cannot end
        // up with three different ideas of what "delete" reaches.
        // The capability is part of CanExecute, not only of whether a button is shown: the Delete
        // KEY runs this command straight from the list, so a check that lived in the chrome would be
        // one keystroke away from being bypassed.
        DeleteOrderCommand = new RelayCommand(
            async _ => await ConfirmAndDeleteSelectedAsync(),
            _ => HasSelection && AuthenticationService.Instance.CanDeleteOrders);
        CopyOrderCommand = new RelayCommand(
            async _ => await CopySelectedAsync(),
            _ => HasSelection && AuthenticationService.Instance.CanCopyOrders);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        StatusMessage = _localization["Status.Ready"];
        OnPropertyChanged(nameof(PageSummary));
        OnPropertyChanged(nameof(FilteredCount));
        // Written by Format rather than bound to a key, so it does not follow a language switch on
        // its own.
        OnPropertyChanged(nameof(SelectionSummary));
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

    /// <summary>
    /// The anchor row — the one the detail panel describes and the one every single-record action
    /// (open, print) works on. It is always part of <see cref="SelectedOrders"/> when anything is
    /// selected at all.
    /// </summary>
    public Order? SelectedOrder
    {
        get => _selectedOrder;
        set { _selectedOrder = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Every row currently selected on the open page. Pushed in by the view: a ListView's
    /// <c>SelectedItems</c> is not a dependency property and so cannot be bound, which is why this
    /// is set through <see cref="SetSelection"/> rather than declared in XAML.
    /// </summary>
    public IReadOnlyList<Order> SelectedOrders => _selectedOrders;

    public int SelectionCount => _selectedOrders.Count;

    public bool HasSelection => _selectedOrders.Count > 0;

    /// <summary>
    /// Whether more than one record is selected — the state in which only Copy and Delete are
    /// offered. Everything else is gated on <see cref="HasSingleSelection"/> instead: opening or
    /// printing "the" order is not a question a multiple selection has an answer to.
    /// </summary>
    public bool HasBatchSelection => _selectedOrders.Count > 1;

    public bool HasSingleSelection => _selectedOrders.Count == 1;

    /// <summary>The count badge shown while a batch is armed, so the reach of Delete is on screen.</summary>
    public string SelectionSummary => _localization.Format("Main.SelectedCount", SelectionCount);

    /// <summary>
    /// Records what the view has selected. Deliberately one-way: nothing here writes back to the
    /// list, so a selection change cannot re-enter through the event that reported it.
    /// </summary>
    public void SetSelection(IEnumerable<Order> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        _selectedOrders = selection.Where(order => order is not null).ToList();
        RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedOrders));
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasBatchSelection));
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    /// <summary>
    /// Asks the view to select exactly these rows. Raised after a batch copy so the copies are the
    /// selection, which is what the single-order copy has always done through
    /// <see cref="SelectedOrder"/> and what a batch cannot express that way.
    /// </summary>
    public event EventHandler<IReadOnlyList<Order>>? SelectionRequested;

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
            OnPropertyChanged(nameof(FilteredCount));
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
            OnPropertyChanged(nameof(FilteredCount));
            OnPropertyChanged(nameof(CanGoToPreviousPage));
            OnPropertyChanged(nameof(CanGoToNextPage));
        }
    }

    public IReadOnlyList<int> PageSizeOptions { get; } = new[] { 20, 50, 100 };

    public string PageSummary => _localization.Format("Paging.Summary", CurrentPage, TotalPages, _filteredCount);

    /// <summary>
    /// How many orders match the current search and status filter, across every page — the badge
    /// beside the records heading. Kept in step with <see cref="PageSummary"/>, which is derived
    /// from the same count.
    /// </summary>
    public int FilteredCount => _filteredCount;

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
        // Refused at the SOURCE rather than by hiding the list. A role without this capability is
        // not supposed to know what the shop's customers are called, and a screen that loads every
        // record and then declines to draw it has already handed them over — to a screenshot, to a
        // memory dump, to the next feature that binds the collection somewhere else.
        if (!AuthenticationService.Instance.CanViewOrders)
        {
            _allOrders.Clear();
            RebuildOrdersView();
            StatusMessage = _localization["Status.Ready"];
            return;
        }

        try
        {
            StatusMessage = _localization["Status.LoadingOrders"];
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Ordered by the day the customer is coming back, soonest first — the list is a work
            // queue now, not a history. Ascending, because the thing a shop needs at the top is what
            // is nearly due, and the row's colour only means something when the urgent end is the
            // end you are looking at.
            //
            // Orders with no pickup date sink to the bottom rather than to the top: every order
            // saved before the field existed has none, and sorting nulls first would bury today's
            // work under years of history. Sorted client-side on purpose — SQLite orders a NULL
            // first whatever the provider translates, and the second key needs the same list.
            var orders = await db.Orders
                .Include(o => o.Items)
                .ToListAsync();

            // FINISHED orders sink, whatever day they were promised for. Without this line the top of
            // the list fills with work that is already done: a job collected last month has last
            // month's pickup date, which sorts it ahead of everything due this week. Rendering the
            // demo data showed exactly that — eight completed and cancelled orders above every
            // overdue one. The list is a queue, so the top of it has to be what is still owed.
            orders = orders
                .OrderBy(o => o.IsPickedUp || o.IsRefunded)
                .ThenBy(o => o.ExpectedPickupDate is null)
                .ThenBy(o => o.ExpectedPickupDate)
                .ThenByDescending(o => o.LastModifiedDate ?? o.OrderDate)
                .ToList();

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
        // DateTime.MaxValue for an order with no pickup date, so clicking this column sends them to
        // the bottom ascending — the same place the default order puts them. Comparer<object> cannot
        // be handed a null.
        nameof(Order.ExpectedPickupDate) => order => order.ExpectedPickupDate ?? DateTime.MaxValue,
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

        // A multiple selection does not survive the page changing under it. Ctrl+A means "this
        // page", so a selection carried through a search, a sort or a page turn would leave Delete
        // reaching rows that are no longer on screen. The view re-pushes its own selection straight
        // afterwards; this is what keeps the two in step for a caller with no window attached.
        SetSelection(SelectedOrder is null ? Array.Empty<Order>() : new[] { SelectedOrder });

        OnPropertyChanged(nameof(PageSummary));
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
    }

    /// <summary>
    /// Asks before deleting the selection, then deletes it. The dialog lives here and nowhere else,
    /// so the action bar, the context menu and the Delete key are all covered by one confirmation.
    /// </summary>
    /// <remarks>
    /// Split from <see cref="DeleteSelectedAsync"/> on purpose: a MessageBox reached from inside the
    /// work blocks the thread, so a harness driving a batch delete would hang on a dialog nothing
    /// can answer. Same shape as <c>TryValidateForSave</c> / <c>ValidateForSave</c> in the order
    /// form.
    /// </remarks>
    private async Task ConfirmAndDeleteSelectedAsync()
    {
        if (!HasSelection) return;

        // One record is named; several are counted. Listing twenty order numbers in a message box
        // is unreadable, and the count is the fact that decides the answer.
        var message = SelectionCount == 1
            ? _localization.Format("Delete.ConfirmMessage", _selectedOrders[0].OrderNumber)
            : _localization.Format("Delete.ConfirmMessageCount", SelectionCount);

        var result = System.Windows.MessageBox.Show(
            message,
            _localization["Delete.ConfirmTitle"],
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        await DeleteSelectedAsync();
    }

    /// <summary>
    /// Deletes every selected order and reloads the list. Returns how many rows were actually
    /// removed, which is not always the count that was selected — another machine, or another
    /// window, may have deleted one already.
    /// </summary>
    public async Task<int> DeleteSelectedAsync()
    {
        var ids = _selectedOrders.Select(order => order.Id).Distinct().ToList();
        if (ids.Count == 0) return 0;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // A query, not FindAsync: Find is a key lookup and bypasses the shop query filter, so a
            // stale selection left over from a shop switch could delete another shop's order. The
            // whole batch goes in ONE SaveChanges — a failure part way through then leaves the list
            // exactly as it was, rather than half deleted with no record of where it stopped.
            var orders = await db.Orders.Where(order => ids.Contains(order.Id)).ToListAsync();
            if (orders.Count > 0)
            {
                db.Orders.RemoveRange(orders);
                await db.SaveChangesAsync();
            }

            var deletedNumber = orders.Count == 1 ? orders[0].OrderNumber : null;
            await LoadOrdersAsync();

            StatusMessage = deletedNumber is not null
                ? _localization.Format("Status.Deleted", deletedNumber)
                : _localization.Format("Status.DeletedCount", orders.Count);

            return orders.Count;
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("Status.DeleteFailed", ex.Message);
            return 0;
        }
    }

    // Statuses that represent a finished order (Shipped is now also read-only/finalized,
    // same as Completed/Cancelled/Returned). Copying such an order starts a fresh
    // active order, so its status is reset to Processing (which also removes the
    // "picked up" tick, since that flag is derived from the Completed status).
    private static bool IsClosedStatus(OrderStatus status)
        => status is OrderStatus.Shipped or OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.Returned;

    /// <summary>
    /// Copies every selected order and selects the copies. Returns how many were written.
    /// </summary>
    /// <remarks>
    /// Each copy gets its own scope and its own SaveChanges, which looks wasteful next to one
    /// batched write and is not. The next order number is reserved by asking the DATABASE what is
    /// already taken, and EF does not see rows that have been added but not saved — so a single
    /// batched save would hand every copy in the selection the same number, which is the defect
    /// this feature would otherwise have introduced at scale.
    /// </remarks>
    public async Task<int> CopySelectedAsync()
    {
        var ids = _selectedOrders.Select(order => order.Id).Distinct().ToList();
        if (ids.Count == 0) return 0;

        var copyIds = new List<int>();
        string? lastNumber = null;

        try
        {
            var shop = ShopContext.Instance.RequireCurrent();

            foreach (var sourceId in ids)
            {
                var copy = await CopyOneOrderAsync(shop, sourceId);
                if (copy is null)
                    continue;

                copyIds.Add(copy.Id);
                lastNumber = copy.OrderNumber;
            }

            await LoadOrdersAsync();
            SelectCopies(copyIds);

            StatusMessage = copyIds.Count == 1 && lastNumber is not null
                ? _localization.Format("Status.CopySucceeded", lastNumber)
                : _localization.Format("Status.CopiedCount", copyIds.Count);

            return copyIds.Count;
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("Status.CopyFailed", ex.Message);
            return copyIds.Count;
        }
    }

    /// <summary>
    /// Points the list at the orders just written, so a batch copy ends with its copies selected —
    /// which is what a single copy has always done. They sort to the top of the first page, being
    /// the most recently touched, so this normally reaches all of them.
    /// </summary>
    private void SelectCopies(IReadOnlyCollection<int> copyIds)
    {
        if (copyIds.Count == 0)
            return;

        var copies = Orders.Where(order => copyIds.Contains(order.Id)).ToList();
        if (copies.Count == 0)
            return;

        SelectedOrder = copies[0];
        SetSelection(copies);
        SelectionRequested?.Invoke(this, copies);
    }

    private async Task<Order?> CopyOneOrderAsync(Shop shop, int sourceId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var source = await db.Orders
            .Include(o => o.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == sourceId);

        if (source is null)
            return null;

        // The copy takes a number from the shop's OWN receipt run, exactly as a new order does.
        // This used to compose "ORD-{timestamp}" by hand, which ignored whatever prefix and
        // numbering mode the shop had configured — so a shop on sequential numbering got a
        // timestamp number from Copy and nothing else.
        var now = DateTime.Now;
        var copy = new Order
        {
            OrderNumber = OrderNumberFormatter.Reserve(db, shop, now),
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

        // Move the shop's running number past the one just used, exactly as saving a new order
        // does. Without this a sequential run would re-offer the same number to the next order and
        // only Reserve's collision scan would push it along — the counter itself would never move.
        ShopContext.Instance.UpdateActiveShop(
            current => OrderNumberFormatter.CommitSequence(current, copy.OrderNumber, now));

        return copy;
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record StatusFilterOption(OrderStatus? Value);

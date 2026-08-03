using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

    /// <summary>
    /// What the shop is currently looking for. ONE object rather than a field per filter, so the
    /// list, the count badge and the CSV export cannot end up applying different rules — see
    /// <see cref="OrderQuery"/>. The properties below are the binding surface onto it.
    /// </summary>
    private OrderQuery _query = OrderQuery.Empty;

    private DateTime? _fromDate;
    private DateTime? _toDate;
    private StatusFilterOption _selectedStatusFilter;

    /// <summary>
    /// Whether this screen is reading or writing data, and what — bound by the busy overlay.
    /// </summary>
    /// <remarks>
    /// One tracker for the whole view model rather than a flag per operation. Refresh, copy and
    /// delete all end by reloading the list, so they overlap; the tracker counts, and the overlay
    /// lifts when the last of them finishes.
    /// </remarks>
    public BusyTracker Busy { get; } = new();
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

    /// <summary>
    /// The query the list is showing. Assigning it rebuilds the list and resets to page one.
    /// </summary>
    /// <remarks>
    /// Exposed as a whole so the CSV export can take exactly what is on screen. Every filter property
    /// below writes through here rather than holding its own field, which is what stops the two
    /// drifting: before this, the search text and the status filter were separate fields matched by
    /// separate <c>if</c>s inside <c>RebuildOrdersView</c>, and adding a third filter meant adding a
    /// third <c>if</c> in a method the export could not call.
    /// </remarks>
    public OrderQuery Query
    {
        get => _query;
        private set
        {
            if (_query == value)
                return;

            _query = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasQuery));
            _currentPage = 1;
            RebuildOrdersView();
        }
    }

    /// <summary>Whether anything is being filtered — what the "clear" button is enabled by.</summary>
    public bool HasQuery => !_query.IsEmpty;

    public string SearchText
    {
        get => _query.Text ?? string.Empty;
        set => Query = _query with { Text = value };
    }

    /// <summary>Which part of an order the search looks in.</summary>
    public IReadOnlyList<OrderSearchField> SearchFieldOptions { get; } =
        Enum.GetValues<OrderSearchField>();

    public OrderSearchField SearchField
    {
        get => _query.Field;
        set => Query = _query with { Field = value };
    }

    /// <summary>
    /// Only orders taken on or after this day, or null for no lower bound.
    /// </summary>
    /// <remarks>
    /// Two nullable days rather than a <see cref="DateRange"/> on the binding surface, because a date
    /// picker can legitimately be half-filled while the user is still typing the other one. They are
    /// composed into a range — the same model the settlement report uses, so "March" means the same
    /// span on both screens — only once at least one end is set.
    /// </remarks>
    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            if (_fromDate == value)
                return;

            _fromDate = value?.Date;
            OnPropertyChanged();
            ApplyPeriod();
        }
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set
        {
            if (_toDate == value)
                return;

            _toDate = value?.Date;
            OnPropertyChanged();
            ApplyPeriod();
        }
    }

    /// <summary>
    /// Composes the two pickers into the query's period.
    /// </summary>
    /// <remarks>
    /// An open end is filled with the other end's extreme rather than left unbounded: "from the 3rd"
    /// means the 3rd onwards, and <c>DateRange</c> is a closed span by construction. A pair the user
    /// has entered backwards is SWAPPED rather than refused — they meant a span between two days, and
    /// an empty list with no explanation is a worse answer than the obvious one.
    /// </remarks>
    private void ApplyPeriod()
    {
        if (_fromDate is null && _toDate is null)
        {
            Query = _query with { Period = null };
            return;
        }

        var first = _fromDate ?? _toDate!.Value;
        var last = _toDate ?? _fromDate!.Value;

        if (first > last)
            (first, last) = (last, first);

        Query = _query with { Period = DateRange.Custom(first, last) };
    }

    /// <summary>Clears every filter at once.</summary>
    public void ClearQuery()
    {
        _fromDate = null;
        _toDate = null;
        _selectedStatusFilter = StatusFilterOptions[0];

        OnPropertyChanged(nameof(FromDate));
        OnPropertyChanged(nameof(ToDate));
        OnPropertyChanged(nameof(SelectedStatusFilter));
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(SearchField));

        Query = OrderQuery.Empty;
    }

    /// <summary>Every order matching the current query, across every page — what the export takes.</summary>
    public IReadOnlyList<Order> FilteredOrders => _query.Apply(_allOrders);

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
            Query = _query with { Status = value.Value };
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

        // The overlay says the same thing the status bar does, but on top of the list the user is
        // looking at rather than in a line at the foot of the window.
        using var busy = Busy.Begin(_localization["Status.LoadingOrders"]);

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
        // Through the query model, which is the ONE definition of what the shop is looking at. The
        // three `if`s that used to live here were the same rule written a second time, and the CSV
        // export could not have called them.
        var filtered = _query.Apply(_allOrders);
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
    /// Sends every selected order to the recycle bin and reloads the list. Returns how many rows
    /// actually moved, which is not always the count that was selected — another window may have
    /// deleted one already.
    /// </summary>
    /// <remarks>
    /// Since v8.0 this REMOVES NOTHING. It routes through <see cref="OrderRecycleBin.Delete"/>, which
    /// stamps <c>DeletedOnUtc</c> and leaves the row where it is; the query filter takes it off every
    /// screen and the retention window decides when it really goes. The wording the user sees changed
    /// with it — see <c>Delete.ConfirmMessage</c> — because a confirmation that still says
    /// "permanently" would be describing a behaviour the application no longer has, and the next
    /// person to read it would trust the message over the code.
    /// </remarks>
    public async Task<int> DeleteSelectedAsync()
    {
        var ids = _selectedOrders.Select(order => order.Id).Distinct().ToList();
        if (ids.Count == 0) return 0;

        var deletedNumber = ids.Count == 1 ? _selectedOrders[0].OrderNumber : null;

        using var busy = Busy.Begin(_localization["Busy.Deleting"]);

        try
        {
            int moved;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                moved = OrderRecycleBin.Delete(db, ids, DateTime.UtcNow);
            }

            if (moved != 1)
                deletedNumber = null;

            await LoadOrdersAsync();

            StatusMessage = deletedNumber is not null
                ? _localization.Format("Status.Deleted", deletedNumber)
                : _localization.Format("Status.DeletedCount", moved);

            return moved;
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.Format("Status.DeleteFailed", ex.Message);
            return 0;
        }
    }

    /// <summary>
    /// Builds a spreadsheet of EVERY order this shop holds, and a file name for it.
    /// </summary>
    /// <remarks>
    /// The whole set, not the filtered one and not the visible page. The button says so — "Export all
    /// orders" — because the one thing an export must not be is ambiguous about its own scope: a file
    /// with fewer rows than the shop has is one somebody quietly bases a quarter's accounts on, and
    /// nobody re-counts a spreadsheet.
    ///
    /// The recycle bin is still excluded, and that is not a filter but the query filter itself: a
    /// deleted order is not one of the shop's orders until it is restored.
    ///
    /// Returns the writer rather than saving it: choosing where a file goes means a dialog, and a
    /// view model that opens one cannot be driven by a harness. The window owns the dialog, this owns
    /// the content — the same split as the delete confirmation above.
    /// </remarks>
    public (CsvWriter Csv, string FileName, int RowCount) BuildOrderExport()
    {
        var orders = _allOrders;

        return (
            OrderCsvExport.Build(orders, _localization),
            OrderCsvExport.SuggestFileName(
                ShopContext.Instance.Current, _localization.CurrentLanguageCode, orders.Count),
            orders.Count);
    }

    /// <summary>
    /// Reports the outcome of an export on the status bar, in one place for both paths.
    /// </summary>
    /// <remarks>
    /// The success message arrives already composed (the caller knows the row count and the path),
    /// while the failure wraps a raw exception message in a sentence. A pass-through
    /// <c>"{0}"</c> key for the success half would be a string-table entry identical in every
    /// language, which is indistinguishable from a translation that was never done.
    /// </remarks>
    public void ReportExport(bool succeeded, string detail)
        => StatusMessage = succeeded ? detail : _localization.Format("Csv.Export.Failed", detail);

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
    public Task<int> CopySelectedAsync()
        => CopyOrdersAsync(_selectedOrders.Select(order => order.Id).Distinct().ToList());

    /// <summary>
    /// Copies an explicit set of orders and selects the copies. Returns how many were written.
    /// </summary>
    /// <remarks>
    /// Takes IDS rather than the orders themselves because the two callers hold different things: the
    /// Copy action copies what is selected right now, while a Ctrl+V pastes what Ctrl+C put on
    /// <c>AppClipboard</c> — records that may since have been paged away, re-sorted or filtered out
    /// of the list. An id survives all of that, and an id that has since been deleted simply copies
    /// nothing.
    /// </remarks>
    public async Task<int> CopyOrdersAsync(IReadOnlyList<int> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0) return 0;

        var copyIds = new List<int>();
        string? lastNumber = null;

        using var busy = Busy.Begin(_localization["Busy.Copying"]);

        try
        {
            var shop = ShopContext.Instance.RequireCurrent();

            // Read once for the whole batch and grown as copies are written. Read once PER COPY it
            // would be correct too and would ask the database the same question n times; not grown,
            // two copies made in one click would both come out "- Copy 1".
            var takenNames = ReadCustomerNames(shop);

            foreach (var sourceId in ids)
            {
                var copy = await CopyOneOrderAsync(shop, sourceId, takenNames);
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

    /// <summary>
    /// Every customer name this shop holds, for <see cref="OrderCopyName"/> to number a copy against.
    /// </summary>
    /// <remarks>
    /// <c>IgnoreQueryFilters</c> with the shop restated by hand, the same reading as
    /// <c>OrderNumberFormatter.IsTaken</c>: the DELETION half must go, because an order in the
    /// recycle bin can be restored at any point in the retention window and would then sit beside a
    /// copy claiming to be the same one; the SHOP half must stay, since another branch's customers
    /// are no reason to number this branch's copies differently.
    /// </remarks>
    private HashSet<string> ReadCustomerNames(Shop shop)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return new HashSet<string>(
            db.Orders.IgnoreQueryFilters()
                .Where(order => order.ShopId == shop.Id)
                .Select(order => order.CustomerName),
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Order?> CopyOneOrderAsync(Shop shop, int sourceId, ICollection<string> takenNames)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var source = await db.Orders
            .Include(o => o.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == sourceId);

        if (source is null)
            return null;

        // The copy is marked on the CUSTOMER name, not on the order number: the number is drawn from
        // the shop's receipt run and is printed on a slip somebody carries, so it carries no
        // decoration. Claimed before the save, so a failure part way cannot re-issue the same one.
        var copyName = OrderCopyName.Next(source.CustomerName, takenNames, _localization);
        takenNames.Add(copyName);

        // The number comes from the shop's OWN receipt run, exactly as a new order's does. This used
        // to compose "ORD-{timestamp}" by hand, which ignored whatever prefix and numbering mode the
        // shop had configured.
        //
        // WHAT the copy inherits is OrderDuplicate's to decide, and it projects from the EF model
        // rather than listing columns here — the list that used to live in this method had silently
        // stopped copying the pricing mode, the per-stage tax rates and the payment split.
        var now = DateTime.Now;
        var copy = OrderDuplicate.Build(db, source, OrderNumberFormatter.Reserve(db, shop, now),
            copyName, DateTime.UtcNow);

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

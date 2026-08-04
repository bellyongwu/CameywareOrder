using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics.CodeAnalysis;
using CameywareOrder.Controls;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using CameywareOrder.ViewModels;
using CameywareOrder.Views;

using System.Windows.Controls.Primitives;

namespace CameywareOrder;

public partial class MainWindow
{
    // The order list itself: selection, sorting, paging, the keyboard, the row context menu, and the advanced-search disclosure. Opening and editing an order lives here; what an order CONTAINS does not.

    // Shipped is treated as a finalized/completed state (the order has already been
    // delivered to the customer), so it is read-only just like Completed/Cancelled/Returned.
    private static bool IsReadOnlyStatus(OrderStatus status)
        => status is OrderStatus.Shipped or OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.Returned;

    private void RefreshToolbarLabels()
    {
        var selectedStatus = _viewModel.SelectedOrder?.Status;
        var isReadOnly = selectedStatus.HasValue && IsReadOnlyStatus(selectedStatus.Value);

        // "View" rather than "Edit" for a finished order — and for anyone whose role does not let
        // them change one, which is the same fact about the same button.
        var label = _localization[isReadOnly || !AuthenticationService.Instance.CanEditOrders
            ? "Toolbar.ViewOrder"
            : "Toolbar.EditOrder"];

        EditOrderButton.Content = label;
        EditContextMenuItem.Header = label;

        RefreshOrderActions();
    }

    /// <summary>
    /// Shows or hides the per-order actions, by capability and by what the selected order is.
    /// </summary>
    /// <remarks>
    /// The two measurement entries carry BOTH conditions, which is why they are set here rather than
    /// bound in XAML: a code-set <c>Visibility</c> replaces a binding instead of combining with it,
    /// so leaving the old <c>HasCustomMadeService</c> binding in place and adding a capability gate
    /// on top would have produced a menu item that obeyed whichever rule had written to it last.
    /// </remarks>
    private void RefreshOrderActions()
    {
        var auth = AuthenticationService.Instance;

        NewOrderButton.Visibility = Show(auth.CanCreateOrders);
        DeleteOrderButton.Visibility = Show(auth.CanDeleteOrders);
        CopyContextMenuItem.Visibility = Show(auth.CanCopyOrders);
        DeleteContextMenuItem.Visibility = Show(auth.CanDeleteOrders);

        var print = auth.CanPrintOrderDocuments;
        var measurements = print && _viewModel.SelectedOrder?.HasCustomMadeService is true;

        PrintMenuItem.Visibility = Show(print);
        PrintContextSeparator.Visibility = Show(print);
        PrintReceiptContextMenuItem.Visibility = Show(print);
        PrintMeasurementsMenuItem.Visibility = Show(measurements);
        PrintReceiptAndMeasurementsMenuItem.Visibility = Show(measurements);
        PrintMeasurementsContextMenuItem.Visibility = Show(measurements);
        PrintBothContextMenuItem.Visibility = Show(measurements);
    }

    /// <summary>
    /// Hands the list's selection to the view model, which is what Copy and Delete act on.
    /// </summary>
    /// <remarks>
    /// A ListView's <c>SelectedItems</c> is not a dependency property, so it cannot be bound and has
    /// to be pushed. One direction only: the view model never writes back from here, so reporting a
    /// selection cannot re-enter through the event that reported it.
    /// </remarks>
    private void OnOrdersSelectionChanged(object sender, SelectionChangedEventArgs e)
        => _viewModel.SetSelection(OrdersListView.SelectedItems.OfType<Order>());

    /// <summary>
    /// Selects exactly the rows the view model asks for — used after a batch copy, so the copies
    /// end up selected the way a single copy has always left its copy selected.
    /// </summary>
    private void OnSelectionRequested(object? sender, IReadOnlyList<Order> orders)
    {
        OrdersListView.SelectedItems.Clear();
        foreach (var order in orders)
            OrdersListView.SelectedItems.Add(order);

        if (orders.Count > 0)
            OrdersListView.ScrollIntoView(orders[0]);
    }

    private void OnOrderRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Exactly one row: a double-click that lands on a batch would open whichever order happened
        // to be the anchor, silently ignoring the rest of what is selected.
        if (!_viewModel.HasSingleSelection)
            return;

        OnEditOrderClick(sender, new RoutedEventArgs());
    }

    /// <summary>
    /// Left and Right arrow page the order list, from anywhere in the window.
    /// </summary>
    /// <remarks>
    /// Accessibility: paging was reachable only by clicking two small buttons at the bottom of the
    /// list. Arrow keys give the whole list keyboard-only navigation without a modifier chord to
    /// memorise, and the page summary is a polite live region so a screen reader says where you
    /// landed.
    ///
    /// PreviewKeyDown on the window rather than a <c>KeyBinding</c>: an InputBinding fires no matter
    /// what has focus, which would page the list every time someone moved the caret in the search
    /// box. Handling it here lets <see cref="ConsumesHorizontalArrows"/> stand down for controls
    /// that own the key.
    ///
    /// Any modifier stands down too — Alt+Left is "back" almost everywhere, and Ctrl+Left is
    /// word-wise caret movement. Neither should be quietly redefined as "previous page".
    /// </remarks>
    private void OnMainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // ESC is "I am leaving this machine". It offers the choice rather than acting, because the
        // two things it can mean — lock, or sign out — are not interchangeable and the key is easy
        // to hit by accident.
        //
        // Modifier-free only, and only from the window itself: a combo box or a context menu that is
        // open owns ESC to close itself, and it is already handled by the time this tunnels past.
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None && !e.Handled)
        {
            e.Handled = true;
            OfferSessionChoice();
            return;
        }

        // F5 reloads the list, from anywhere in the window and whatever has focus. Unlike the arrow
        // keys below it needs no stand-down list: no text box, combo or picker in this application
        // claims F5, and every Windows user tries it before looking for a button — which is what let
        // the Refresh button give up its place in the action bar to the export.
        if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;

            if (_viewModel.LoadOrdersCommand.CanExecute(null))
                _viewModel.LoadOrdersCommand.Execute(null);

            return;
        }

        if (e.Key is not (Key.Left or Key.Right))
            return;

        if (Keyboard.Modifiers != ModifierKeys.None)
            return;

        if (ConsumesHorizontalArrows(Keyboard.FocusedElement as DependencyObject))
            return;

        var command = e.Key == Key.Right ? _viewModel.NextPageCommand : _viewModel.PreviousPageCommand;
        if (!command.CanExecute(null))
            return;

        e.Handled = true;
        command.Execute(null);

        AnnouncePageChange();
        FocusFirstOrder();
    }

    /// <summary>
    /// Whether the focused element, or anything it sits inside, needs the horizontal arrows itself.
    /// </summary>
    /// <remarks>
    /// Walks up the tree because focus usually lands on a part inside the control — the editable
    /// TextBox of a ComboBox, or a DatePicker's inner text box — and testing only the focused
    /// element would miss it and steal the key anyway.
    /// </remarks>
    private static bool ConsumesHorizontalArrows(DependencyObject? focused)
    {
        for (var node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TextBoxBase or PasswordBox or ComboBox or DatePicker or Slider or MenuBase)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Nudges the live region so a screen reader re-reads it. Rebinding the text alone does not
    /// raise the event, so the announcement has to be asked for explicitly.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "False positive: PageSummaryText is an x:Name instance field from the XAML-generated " +
                        "partial, which the analyzer does not see. The method reads instance data and cannot be static.")]
    private void AnnouncePageChange()
    {
        var peer = UIElementAutomationPeer.FromElement(PageSummaryText)
            ?? UIElementAutomationPeer.CreatePeerForElement(PageSummaryText);

        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    /// <summary>
    /// Puts selection and focus on the first row of the freshly loaded page.
    /// </summary>
    /// <remarks>
    /// Without this the keyboard user is left on a page whose rows they cannot reach with Up/Down
    /// until they Tab back into the list, and a screen reader has nothing to read. The container is
    /// generated asynchronously, so this waits for the item containers rather than assuming them.
    /// </remarks>
    private void FocusFirstOrder()
    {
        if (OrdersListView.Items.Count == 0)
            return;

        OrdersListView.SelectedIndex = 0;
        OrdersListView.ScrollIntoView(OrdersListView.Items[0]);

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (OrdersListView.ItemContainerGenerator.ContainerFromIndex(0) is ListViewItem row)
                    row.Focus();
            }),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // Enter opens the selected order, as a double-click does. Delete removes the whole selection.
    //
    // The two keys are gated differently on purpose: Enter opens ONE order and so needs exactly one
    // selected, while Delete is a batch action and the command it routes to owns both the "one" and
    // the "several" wording. Ctrl+A is not handled here at all — ListBox has its own SelectAll
    // command binding on that gesture, live in Extended mode, and it reaches the loaded items,
    // which is the current page.
    private void OnOrderRowKeyDown(object sender, KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        switch (e.Key)
        {
            case Key.Enter:
                if (!_viewModel.HasSingleSelection)
                    return;
                e.Handled = true;
                OnEditOrderClick(sender, new RoutedEventArgs());
                break;
            case Key.Delete:
                e.Handled = true;
                if (_viewModel.DeleteOrderCommand.CanExecute(null))
                    _viewModel.DeleteOrderCommand.Execute(null);
                break;
        }
    }

    /// <summary>
    /// Makes a right-click land on the row the pointer is over. WPF does not select on right-click,
    /// so without this the context menu would act on whatever was selected before.
    /// </summary>
    /// <remarks>
    /// Right-clicking INSIDE an existing selection leaves it alone — that is how the menu comes to
    /// act on a batch. Right-clicking outside it means "this one instead", so the selection is
    /// REPLACED rather than extended: setting <c>IsSelected</c> on its own adds the row in Extended
    /// mode, and a Delete that quietly takes one more record than the user pointed at is the worst
    /// version of this control.
    /// </remarks>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Named from XAML (EventSetter Handler=\"OnOrderRowRightClick\"). The generated " +
                        "InitializeComponent wires it as this.OnOrderRowRightClick, which does not compile " +
                        "against a static method.")]
    private void OnOrderRowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListViewItem item || item.IsSelected)
            return;

        OrdersListView.SelectedItems.Clear();
        item.IsSelected = true;
    }

    // Keeps the trailing (Notes) column filling the remaining width as the list resizes.
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Named from XAML (SizeChanged=\"OnOrdersListSizeChanged\"); see OnOrderRowRightClick.")]
    private void OnOrdersListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not ListView { View: GridView grid } list || grid.Columns.Count == 0)
            return;

        double used = 0;
        for (int i = 0; i < grid.Columns.Count - 1; i++)
            used += grid.Columns[i].ActualWidth;

        double remaining = list.ActualWidth - used - 28; // account for border + scrollbar
        if (remaining > 200)
            grid.Columns[^1].Width = remaining;
    }

    // Sort the orders list when a sortable column header is clicked. Each click toggles
    // ascending/descending on that column; clicking a new column starts ascending.
    private void OnOrderColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header
            || header.Role == GridViewColumnHeaderRole.Padding
            || header.Column is null)
            return;

        var sortKey = OrderColumnSort.GetSortKey(header.Column);
        if (string.IsNullOrEmpty(sortKey))
            return;

        _viewModel.SortBy(sortKey);
        UpdateSortGlyphs();
    }

    // Reflect the active sort on the column headers with an up/down arrow glyph.
    private void UpdateSortGlyphs()
    {
        if (OrdersListView.View is not GridView grid)
            return;

        var arrow = _viewModel.SortAscending ? " \u25B2" : " \u25BC";
        foreach (var column in grid.Columns)
        {
            var key = OrderColumnSort.GetSortKey(column);
            OrderColumnSort.SetSortGlyph(column,
                !string.IsNullOrEmpty(key) && key == _viewModel.SortKey ? arrow : string.Empty);
        }
    }

    private void OnContextEditClick(object sender, RoutedEventArgs e)
        => OnEditOrderClick(sender, e);

    private void OnContextCopyClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CopyOrderCommand.CanExecute(null))
            _viewModel.CopyOrderCommand.Execute(null);
    }

    private void OnContextDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.DeleteOrderCommand.CanExecute(null))
            _viewModel.DeleteOrderCommand.Execute(null);
    }

    private void OnContextPrintClick(object sender, RoutedEventArgs e)
        => OnPrintReceiptClick(sender, e);

    private void OnContextPrintMeasurementsClick(object sender, RoutedEventArgs e)
        => OnPrintMeasurementsClick(sender, e);

    private void OnContextPrintReceiptAndMeasurementsClick(object sender, RoutedEventArgs e)
        => OnPrintReceiptAndMeasurementsClick(sender, e);

    private void OnAddOrderClick(object sender, RoutedEventArgs e)
    {
        // Gated here as well as by hiding the button. The chrome answers "is this offered"; this
        // answers "may it happen", and only the second one survives a new call site.
        if (!AuthenticationService.Instance.CanCreateOrders)
            return;

        var dialog = new OrderEditWindow(_scopeFactory, _localization) { Owner = this };
        if (dialog.ShowDialog() == true)
            _ = _viewModel.LoadOrdersAsync();
    }

    private void OnEditOrderClick(object sender, RoutedEventArgs e)
    {
        // The shared entry point for the button, the context menu, Enter and the double-click, so
        // the "exactly one row" rule is restated here rather than trusted to four callers.
        if (!_viewModel.HasSingleSelection || _viewModel.SelectedOrder is null) return;
        var dialog = new OrderEditWindow(_scopeFactory, _localization, _viewModel.SelectedOrder) { Owner = this };
        if (dialog.ShowDialog() == true)
            _ = _viewModel.LoadOrdersAsync();
    }

    private static void InsertAtTop(FlowDocument document, Block block)
    {
        var anchor = document.Blocks.FirstBlock;
        if (anchor is null)
            document.Blocks.Add(block);
        else
            document.Blocks.InsertBefore(anchor, block);
    }

    // ── Search, export and the two data panels (v8.0) ─────────────────────────────────────────────

    private void OnClearFiltersClick(object sender, RoutedEventArgs e) => _viewModel.ClearQuery();

    private void OnToggleAdvancedSearchClick(object sender, RoutedEventArgs e)
    {
        _advancedSearchOpen = !_advancedSearchOpen;
        RefreshAdvancedSearch();
    }

    /// <summary>
    /// Shows or hides the second filter row, and says on the button what is behind it.
    /// </summary>
    /// <remarks>
    /// The button carries a MARK when the collapsed row holds an active filter. Without it a list
    /// narrowed by a date range the user set an hour ago, then collapsed, reads as a list that has
    /// lost half its orders — the worst kind of bug report, because nothing on screen is wrong.
    ///
    /// Since v9.5.0 that mark is load-bearing on the very first frame, not only after somebody sets
    /// a filter: the list OPENS on the current month and the period control that says so now lives
    /// inside this panel. The mark is the whole of what a shop sees telling it the list is narrowed.
    ///
    /// Written from code rather than bound, so it has to be re-run from
    /// <see cref="OnLanguageChangedGlobally"/> like every other code-written label here, and from the
    /// view model's <c>Query</c> change so the mark appears the moment a filter is set.
    /// </remarks>
    private void RefreshAdvancedSearch()
    {
        AdvancedFilterPanel.Visibility = _advancedSearchOpen ? Visibility.Visible : Visibility.Collapsed;

        // The caret points where pressing it will GO, which is the convention every disclosure
        // control on Windows follows.
        var caret = _advancedSearchOpen ? " ▴" : " ▾";        
        AdvancedSearchButton.Content = _localization["Search.Advanced"] + caret;
    }
}

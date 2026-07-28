using System.Collections.ObjectModel;
using System.Windows;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;

namespace CameywareOrder.Views;

/// <summary>
/// Edits the open shop's ready-made product catalogue: which categories its order editor offers, in
/// what order, and what user-added ones are called in each language.
/// </summary>
/// <remarks>
/// Per-language naming is delegated to <see cref="MeasurementTermLanguageWindow"/> rather than
/// reimplemented. That dialog already asks "what is this called in each installed language", and its
/// garment mode — constructed without a gender — is exactly this case. Reusing it also means a
/// language added to the application later appears in both editors at once.
///
/// Administrators and the shop's manager; the caller gates it (<c>CanConfigureShop</c>), as every
/// other configuration window here does.
/// </remarks>
public partial class ProductCatalogWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly ObservableCollection<CatalogRow> _rows = new();

    public ProductCatalogWindow(LocalizationService localization)
    {
        InitializeComponent();

        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        CategoryList.ItemsSource = _rows;

        Reload(selectId: null);
    }

    private void Reload(string? selectId)
    {
        _rows.Clear();

        foreach (var item in ProductCatalogService.Instance.Items)
            _rows.Add(new CatalogRow(item, ProductCatalogService.Instance.ResolveName(item.Id)));

        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        CategoryList.SelectedItem = _rows.FirstOrDefault(row =>
            string.Equals(row.Id, selectId, StringComparison.Ordinal)) ?? _rows.FirstOrDefault();

        RefreshButtons();
    }

    private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => RefreshButtons();

    /// <summary>
    /// Enables only what the selection actually supports, rather than letting a click fail. A
    /// predefined category cannot be renamed — its name is the string table's, not the shop's.
    /// </summary>
    private void RefreshButtons()
    {
        var selected = CategoryList.SelectedItem as CatalogRow;
        var index = selected is null ? -1 : _rows.IndexOf(selected);

        RenameButton.IsEnabled = selected is not null && !selected.IsPredefined;
        RemoveButton.IsEnabled = selected is not null;
        MoveUpButton.IsEnabled = index > 0;
        MoveDownButton.IsEnabled = index >= 0 && index < _rows.Count - 1;
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new MeasurementTermLanguageWindow(
            _localization["ProductCatalog.NewCategory"],
            new Dictionary<string, string>())
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        // A generated id, never the typed name: the name is per language and editable, while the id
        // is what every order stores forever. Deriving one from the text would change meaning the
        // moment somebody corrected a typo.
        var item = new ProductItem
        {
            Id = $"custom-{Guid.NewGuid():N}",
            IsPredefined = false,
            Names = new Dictionary<string, string>(dialog.Result)
        };

        if (!ProductCatalogService.Instance.Add(item))
        {
            MessageBox.Show(this, _localization["ProductCatalog.Duplicate"],
                _localization["ProductCatalog.Title"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Reload(item.Id);
    }

    private void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (CategoryList.SelectedItem is not CatalogRow row || row.IsPredefined)
            return;

        var dialog = new MeasurementTermLanguageWindow(row.DisplayName, row.Item.Names) { Owner = this };

        if (dialog.ShowDialog() != true)
            return;

        ProductCatalogService.Instance.Rename(row.Id, dialog.Result);
        Reload(row.Id);
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (CategoryList.SelectedItem is not CatalogRow row)
            return;

        var answer = MessageBox.Show(
            this,
            _localization.Format("ProductCatalog.RemoveConfirm", row.DisplayName),
            _localization["ProductCatalog.Remove"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        ProductCatalogService.Instance.Remove(row.Id);
        Reload(selectId: null);
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e) => Move(-1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) => Move(1);

    private void Move(int offset)
    {
        if (CategoryList.SelectedItem is not CatalogRow row)
            return;

        if (ProductCatalogService.Instance.Move(row.Id, offset))
            Reload(row.Id);
    }

    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            _localization["ProductCatalog.RestoreConfirm"],
            _localization["ProductCatalog.RestoreDefault"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        ProductCatalogService.Instance.RestoreDefaults();
        Reload(selectId: null);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>One row of the list: the category, its resolved name, and whether it is locked.</summary>
    private sealed class CatalogRow
    {
        public CatalogRow(ProductItem item, string displayName)
        {
            Item = item;
            DisplayName = displayName;
        }

        public ProductItem Item { get; }

        public string Id => Item.Id;

        public bool IsPredefined => Item.IsPredefined;

        public string DisplayName { get; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound in ProductCatalogWindow.xaml as the preset badge's Visibility; " +
                            "XAML bindings are invisible to Roslyn. Removing it would show the " +
                            "\"preset\" badge on every row, including the shop's own categories.")]
        public Visibility PresetVisibility => IsPredefined ? Visibility.Visible : Visibility.Collapsed;
    }
}

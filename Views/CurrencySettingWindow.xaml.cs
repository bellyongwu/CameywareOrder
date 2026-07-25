using System.Windows;
using System.Windows.Controls;
using LeeYongeOrdering.Localization;
using LeeYongeOrdering.Models;
using LeeYongeOrdering.Services;

namespace LeeYongeOrdering.Views;

/// <summary>
/// Small dialog that edits the application-wide currency setting (see
/// <see cref="CurrencySettingService"/>). Launched from the 本地配置 toolbar menu.
/// </summary>
public partial class CurrencySettingWindow : Window
{
    private readonly LocalizationService _localization;

    public CurrencySettingWindow(LocalizationService localization)
    {
        InitializeComponent();
        _localization = localization;

        PopulateCurrencies();
        CurrencyBox.SelectedValue = CurrencySettingService.Instance.Current;
    }

    private void PopulateCurrencies()
    {
        CurrencyBox.SelectedValuePath = nameof(ComboBoxItem.Tag);
        CurrencyBox.Items.Add(CreateItem(CurrencyType.CAD, "CurrencyType.CAD"));
        CurrencyBox.Items.Add(CreateItem(CurrencyType.USD, "CurrencyType.USD"));
        CurrencyBox.Items.Add(CreateItem(CurrencyType.CNY, "CurrencyType.CNY"));
    }

    private ComboBoxItem CreateItem(CurrencyType currencyType, string key)
        => new()
        {
            Content = _localization[key],
            Tag = currencyType
        };

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (CurrencyBox.SelectedValue is CurrencyType currencyType)
            CurrencySettingService.Instance.SetCurrency(currencyType);

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
        => DialogResult = false;
}

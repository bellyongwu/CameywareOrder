using System.Windows;
using LeeYongeOrdering.Localization;

namespace LeeYongeOrdering.Views;

public partial class LanguageSelectionWindow : Window
{
    private readonly LocalizationService _localization;

    public string? SelectedLanguageCode { get; private set; }

    public IReadOnlyList<LanguageOption> AvailableLanguages => _localization.AvailableLanguages;

    public string CurrentLanguageCode
    {
        get => _localization.CurrentLanguageCode;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                _localization.SetLanguage(value);
        }
    }

    public LanguageSelectionWindow(LocalizationService localization)
    {
        InitializeComponent();
        _localization = localization;
        DataContext = this;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        SelectedLanguageCode = LanguageBox.SelectedValue?.ToString();
        DialogResult = true;
    }
}

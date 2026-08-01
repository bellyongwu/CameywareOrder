using System.Windows;
using System.Windows.Controls;
using CameywareOrder.Localization;

namespace CameywareOrder.Views;

public partial class LanguageSelectionWindow : Window
{
    private readonly LocalizationService _localization;

    public string? SelectedLanguageCode { get; private set; }

    public LanguageSelectionWindow(LocalizationService localization)
    {
        InitializeComponent();
        _localization = localization;
        SelectedLanguageCode = _localization.CurrentLanguageCode;
        BuildLanguageOptions();
    }

    private void BuildLanguageOptions()
    {
        foreach (var option in _localization.AvailableLanguages)
        {
            var radio = new RadioButton
            {
                Content = option.Name,
                Tag = option.Code,
                GroupName = "LanguageGroup",
                FontSize = 14,
                Margin = new Thickness(0, 6, 0, 6),
                Padding = new Thickness(6, 0, 0, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsChecked = string.Equals(option.Code, _localization.CurrentLanguageCode, StringComparison.OrdinalIgnoreCase)
            };
            radio.Checked += OnLanguageRadioChecked;
            LanguageOptionsPanel.Children.Add(radio);
        }
    }

    // Selecting a language switches the UI immediately so the welcome/prompt text
    // updates live, previewing the chosen language before the user enters the system.
    private void OnLanguageRadioChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string code } && !string.IsNullOrWhiteSpace(code))
        {
            SelectedLanguageCode = code;
            _localization.SetLanguage(code);
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

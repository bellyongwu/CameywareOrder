using System.Windows;
using System.Windows.Controls;
using CameywareOrder.Localization;
using CameywareOrder.Services;

namespace CameywareOrder.Views;

/// <summary>
/// Small dialog that asks for the language and unit to use when printing a custom-made
/// order's measurements. Launched from the print menus on the main window; the caller
/// reads <see cref="SelectedLanguageCode"/> and <see cref="IsInch"/> after a true result.
/// </summary>
public partial class MeasurementPrintOptionsWindow : Window
{
    public MeasurementPrintOptionsWindow(LocalizationService localization)
    {
        InitializeComponent();
        BuildLanguageOptions(localization);
    }

    public string SelectedLanguageCode { get; private set; } = string.Empty;

    public bool IsInch { get; private set; }

    /// <summary>
    /// One radio per language this session may print in — the open shop's installed set, or every
    /// shipped language for an administrator. The SAME scope as the toolbar toggle, through the same
    /// helper: a sheet printed in a language the branch does not run in is one nobody there can
    /// check, and two copies of that rule would drift.
    /// </summary>
    private void BuildLanguageOptions(LocalizationService localization)
    {
        var current = localization.CurrentLanguageCode;
        var selectable = ShopLanguages.Selectable();

        // One language is not a choice. The radio is still created — the print handler reads its
        // Tag — but the prompt and the row are collapsed so the dialog only asks about the unit.
        var visibility = selectable.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        LanguagePromptText.Visibility = visibility;
        LanguageOptionsPanel.Visibility = visibility;

        foreach (var language in selectable)
        {
            var radio = new RadioButton
            {
                Content = language.Name,
                Tag = language.Code,
                GroupName = "MeasurePrintLanguage",
                Margin = new Thickness(0, 0, 0, 6),
                IsChecked = string.Equals(language.Code, current, StringComparison.OrdinalIgnoreCase)
            };
            LanguageOptionsPanel.Children.Add(radio);
        }

        // Guarantee a selection even if the current language code did not match any option.
        if (!LanguageOptionsPanel.Children.OfType<RadioButton>().Any(radio => radio.IsChecked.GetValueOrDefault())
            && LanguageOptionsPanel.Children.OfType<RadioButton>().FirstOrDefault() is { } first)
        {
            first.IsChecked = true;
        }
    }

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        var selected = LanguageOptionsPanel.Children.OfType<RadioButton>()
            .FirstOrDefault(radio => radio.IsChecked.GetValueOrDefault());
        SelectedLanguageCode = selected?.Tag as string ?? string.Empty;
        IsInch = InchRadio.IsChecked.GetValueOrDefault();
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
        => DialogResult = false;
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using CameywareOrder.Localization;
using CameywareOrder.Models;

namespace CameywareOrder.Views;

/// <summary>
/// Small editor that lets the user provide a name for a custom measurement term (or
/// garment) in every available language. This is the "afterwords overriding" popup:
/// the same measurement can be named differently per region/language. When editing a
/// term (not a garment), an optional gender picker is also shown so the term can be
/// declared common or gender-specific.
/// </summary>
public partial class MeasurementTermLanguageWindow : Window
{
    private readonly ObservableCollection<LanguageNameRow> _rows = new();

    /// <summary>Language code → entered name, populated when the dialog is accepted.</summary>
    public Dictionary<string, string> Result { get; private set; } = new();

    /// <summary>
    /// Selected gender classification, populated when the dialog is accepted. Only
    /// meaningful when the gender picker was shown (i.e. <c>initialGender</c> was
    /// provided); otherwise stays at its default (<see cref="MeasurementGender.Common"/>)
    /// and callers editing a garment simply ignore it.
    /// </summary>
    public MeasurementGender GenderResult { get; private set; } = MeasurementGender.Common;

    /// <param name="initialGender">
    /// Pass a value to show the gender picker (editing a measurement term) pre-selected
    /// to this value; pass <c>null</c> to hide it (editing a garment, which has no
    /// gender concept).
    /// </param>
    public MeasurementTermLanguageWindow(string headerName, IReadOnlyDictionary<string, string> currentNames, MeasurementGender? initialGender = null)
    {
        InitializeComponent();

        TermNameText.Text = headerName;

        if (initialGender.HasValue)
        {
            GenderPanel.Visibility = Visibility.Visible;
            PopulateGenders(initialGender.Value);
        }

        foreach (var language in LocalizationService.Instance.AvailableLanguages)
        {
            currentNames.TryGetValue(language.Code, out var existing);
            _rows.Add(new LanguageNameRow(language.Code, language.Name) { Name = existing ?? string.Empty });
        }

        LanguageRows.ItemsSource = _rows;
    }

    /// <summary>
    /// Fills the gender drop-down and selects the term's current classification.
    /// </summary>
    /// <remarks>
    /// Built here rather than declared in XAML so the labels come from the string table at the
    /// moment the dialog opens. It is modal and short-lived, so it never has to survive a language
    /// switch while on screen — the same reasoning the currency picker in ShopSetupWindow uses.
    ///
    /// Common leads the list because it is the default for a new term and by far the common case.
    /// </remarks>
    private void PopulateGenders(MeasurementGender selected)
    {
        var loc = LocalizationService.Instance;

        // Common leads: it is the default for a new term and by far the common case.
        GenderBox.ItemsSource = new[]
        {
            MeasurementGender.Common,
            MeasurementGender.Male,
            MeasurementGender.Female,
        }.Select(gender => new GenderOption(
            gender,
            MeasurementGenderPresentation.NameText(loc, gender),
            MeasurementGenderPresentation.SymbolWithCommon(gender))).ToList();

        GenderBox.SelectedValue = selected;
    }

    /// <summary>
    /// The chosen classification, falling back to Common. The fallback is reachable only if the
    /// drop-down were somehow left unset, and Common is the right answer there: a term with no
    /// stated gender applies to everyone, which is what the picker defaults to anyway.
    /// </summary>
    private MeasurementGender ReadSelectedGender()
        => GenderBox.SelectedValue as MeasurementGender? ?? MeasurementGender.Common;

    /// <summary>
    /// One entry in the gender drop-down: its value, its localized name, and the symbol shown in
    /// front of it — all three taken from <see cref="MeasurementGenderPresentation"/> so the picker
    /// cannot disagree with the badge the terms list draws for the same classification.
    /// </summary>
    private sealed record GenderOption(MeasurementGender Value, string Label, string Symbol);

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var result = new Dictionary<string, string>();
        foreach (var row in _rows.Where(row => !string.IsNullOrWhiteSpace(row.Name)))
            result[row.LanguageCode] = row.Name.Trim();

        if (result.Count == 0)
        {
            MessageBox.Show(
                LocalizationService.Instance["MeasureTerms.NameRequired"],
                LocalizationService.Instance["TermLanguage.Title"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = result;
        GenderResult = ReadSelectedGender();
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class LanguageNameRow : INotifyPropertyChanged
    {
        private string _name = string.Empty;

        public LanguageNameRow(string languageCode, string languageName)
        {
            LanguageCode = languageCode;
            LanguageName = languageName;
        }

        public string LanguageCode { get; }

        public string LanguageName { get; }

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value)
                    return;
                _name = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using LeeYongeOrdering.Localization;

namespace LeeYongeOrdering.Views;

/// <summary>
/// Small editor that lets the user provide a name for a custom measurement term (or
/// garment) in every available language. This is the "afterwords overriding" popup:
/// the same measurement can be named differently per region/language.
/// </summary>
public partial class MeasurementTermLanguageWindow : Window
{
    private readonly ObservableCollection<LanguageNameRow> _rows = new();

    /// <summary>Language code → entered name, populated when the dialog is accepted.</summary>
    public Dictionary<string, string> Result { get; private set; } = new();

    public MeasurementTermLanguageWindow(string headerName, IReadOnlyDictionary<string, string> currentNames)
    {
        InitializeComponent();

        TermNameText.Text = headerName;

        foreach (var language in LocalizationService.Instance.AvailableLanguages)
        {
            currentNames.TryGetValue(language.Code, out var existing);
            _rows.Add(new LanguageNameRow(language.Code, language.Name) { Name = existing ?? string.Empty });
        }

        LanguageRows.ItemsSource = _rows;
    }

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

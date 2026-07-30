using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;

namespace CameywareOrder.Controls;

/// <summary>
/// A phone field that carries the country its number belongs to: a dial-code picker in front of the
/// number, and validation against the country PICKED FOR THAT NUMBER.
/// </summary>
/// <remarks>
/// One control for all five phone fields in the application — the customer on an order, the customer
/// on a custom-made record, the shop's own number, and the two staff screens. They already shared one
/// validator (<see cref="ContactValidation"/>) precisely because a second copy of the rule is free to
/// drift; a second copy of the CONTROL would put the rule back in five places.
///
/// The stored value stays one string ("+86 138 0013 8000"), as it always was. No column was added and
/// no number was rewritten: <c>PhoneCountries.Split</c> reads the country back off the prefix, and
/// anything it does not recognise — every number saved before this existed — comes back whole, under
/// the shop's own country, and is left alone.
/// </remarks>
[SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
    Justification = "Every member here reads CountryBox or NumberBox, x:Name fields declared in the " +
                    "XAML-generated partial. SonarLint's standalone single-file pass cannot see that " +
                    "partial and so reads the whole surface as touching no instance state; the full " +
                    "build, which does see it, reports none of them. They are also the control's public " +
                    "API and are bound per instance, so static is not available as a fix.")]
public partial class PhoneNumberField : UserControl
{
    private readonly LocalizationService _localization = LocalizationService.Instance;
    private bool _populating;

    public PhoneNumberField()
    {
        InitializeComponent();
        Populate(PhoneCountries.Default);
        _localization.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => _localization.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>Raised when either half changes, so a host can clear its own error message as the user types.</summary>
    public event EventHandler? PhoneChanged;

    /// <summary>Raised when the number box loses focus, for hosts that validate at that point.</summary>
    public event EventHandler? PhoneCommitted;

    /// <summary>The country currently picked — what this number is validated against.</summary>
    public PhoneCountry? SelectedCountry => (CountryBox.SelectedItem as CountryRow)?.Country;

    /// <summary>The national part, as typed: no dial code, no reformatting.</summary>
    public string NationalNumber
    {
        get => NumberBox.Text.Trim();
        set => NumberBox.Text = value ?? string.Empty;
    }

    /// <summary>The whole number as it is stored and printed — dial code, a space, then the number.</summary>
    public string FullNumber => PhoneCountries.Compose(SelectedCountry, NumberBox.Text);

    /// <summary>True when the number is blank, which every caller treats as "not given" rather than wrong.</summary>
    public bool IsBlank => NumberBox.Text.Trim().Length == 0;

    /// <summary>Whether what is typed is a possible number in the country picked for it.</summary>
    public bool IsValid => ContactValidation.IsValidNationalPhone(NumberBox.Text, SelectedCountry);

    /// <summary>Whether what is typed passes the LOOSE rule every record saved before this used.</summary>
    /// <remarks>
    /// What an EXISTING record is held to. Tightening retroactively would mean an order from last year
    /// could not be saved again — over a phone number nobody can re-verify — until someone changed it.
    /// </remarks>
    public bool IsValidLoose => ContactValidation.IsValidPhone(FullNumber);

    /// <summary>The message for a number that does not fit its country, naming the digits expected.</summary>
    public string ValidationMessage
    {
        get
        {
            var country = SelectedCountry;
            // Falls back to the message every phone field already used, rather than a second key
            // saying the same thing in slightly different words.
            if (country is null)
                return _localization["OrderEdit.Validate.PhoneInvalid"];

            return _localization.Format("OrderEdit.Validate.PhoneDigits",
                country.DisplayName(_localization),
                country.ExpectedDigitsText(_localization));
        }
    }

    /// <summary>Mirrors <see cref="TextBox.IsReadOnly"/> across both halves, for a read-only form.</summary>
    public bool IsReadOnlyField
    {
        get => NumberBox.IsReadOnly;
        set
        {
            NumberBox.IsReadOnly = value;
            CountryBox.IsEnabled = !value;
        }
    }

    /// <summary>
    /// Shows a stored number, splitting the dial code off the front. <paramref name="shop"/> supplies
    /// the country to open on when the stored value names none — a blank field on a new record, or a
    /// number saved before this control existed.
    /// </summary>
    public void Load(string? stored, Shop? shop)
    {
        var (country, national) = PhoneCountries.Split(stored, PhoneCountries.ForShop(shop));

        _populating = true;
        try
        {
            SelectCountry(country);
            NumberBox.Text = national;
        }
        finally
        {
            _populating = false;
        }
    }

    /// <summary>Points a blank field at the country a shop's numbers usually come from.</summary>
    public void ResetTo(Shop? shop) => Load(null, shop);

    /// <summary>
    /// Follows a location code picked elsewhere on the same form — a shop being moved to Japan should
    /// be offered +81 rather than keep the market it is leaving.
    /// </summary>
    /// <remarks>
    /// Only while the number is BLANK. A shop that has typed its number and then corrects its location
    /// has not asked for its phone number to be re-interpreted, and silently re-coding a real number is
    /// how a shop ends up printing a wrong one on its receipts.
    /// </remarks>
    public void FollowLocation(string? locationCode)
    {
        if (!IsBlank)
            return;

        var country = PhoneCountries.ForShop(new Shop { LocationCode = locationCode });

        _populating = true;
        try
        {
            SelectCountry(country);
        }
        finally
        {
            _populating = false;
        }
    }

    /// <summary>Paints the number box to say it is wrong, or clears that.</summary>
    /// <remarks>
    /// The normal border is read back from the theme rather than written here, so this cannot become
    /// the one control that keeps an old palette. <c>TryFindResource</c> because a harness that merges
    /// no dictionaries must still be able to drive the field.
    /// </remarks>
    public void MarkInvalid(bool invalid)
    {
        if (invalid)
        {
            NumberBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
            NumberBox.BorderThickness = new Thickness(2);
            return;
        }

        NumberBox.BorderBrush = TryFindResource("BorderStrongBrush") as Brush ?? NumberBox.BorderBrush;
        NumberBox.BorderThickness = new Thickness(1);
    }

    /// <summary>Puts the caret in the number, not on the country picker — the field a message is about.</summary>
    public void FocusNumber() => NumberBox.Focus();

    private void SelectCountry(PhoneCountry country)
    {
        foreach (var row in CountryBox.Items.OfType<CountryRow>())
        {
            if (!string.Equals(row.Country.Code, country.Code, StringComparison.OrdinalIgnoreCase))
                continue;

            CountryBox.SelectedItem = row;
            return;
        }

        CountryBox.SelectedIndex = 0;
    }

    /// <summary>
    /// Rebuilds the rows, keeping whatever was picked. Runs on a language change because each row
    /// names its country in the current language — a picker that kept its old names would be the only
    /// stale control on the screen.
    /// </summary>
    private void Populate(PhoneCountry selected)
    {
        _populating = true;
        try
        {
            CountryBox.ItemsSource = PhoneCountries.All
                .Select(country => new CountryRow(country, _localization))
                .ToList();
            SelectCountry(selected);
        }
        finally
        {
            _populating = false;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => Populate(SelectedCountry ?? PhoneCountries.Default);

    private void OnCountryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populating)
            return;

        PhoneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnNumberChanged(object sender, TextChangedEventArgs e)
    {
        if (_populating)
            return;

        PhoneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnNumberLostFocus(object sender, RoutedEventArgs e)
        => PhoneCommitted?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// One row of the picker. The flag is resolved HERE, by key, rather than through a converter: the
    /// lookup is a dictionary hit and a converter would only add a place for it to fail silently.
    /// A country shipped without a flag drawing shows the dial code alone rather than throwing.
    /// </summary>
    private sealed class CountryRow
    {
        public CountryRow(PhoneCountry country, LocalizationService localization)
        {
            Country = country;
            DialCode = country.DialCode;
            Name = country.DisplayName(localization);
            Flag = Application.Current?.TryFindResource($"Flag.{country.Code}") as ImageSource;
        }

        public PhoneCountry Country { get; }
        public string DialCode { get; }
        public string Name { get; }
        public ImageSource? Flag { get; }
    }
}

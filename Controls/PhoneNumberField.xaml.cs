using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    /// <summary>Punctuation a stored number may already carry and still be safe to re-group.</summary>
    private const string KnownSeparators = " -().";

    private readonly LocalizationService _localization = LocalizationService.Instance;
    private bool _populating;
    private bool _formatting;

    /// <summary>What <see cref="Load"/> put on screen, to tell an edit apart from a stored value.</summary>
    private string _loadedNumber = string.Empty;

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

            // Two different problems, and telling somebody "a number here has 10 digits" when they
            // have typed exactly 10 is worse than saying nothing: it reads as a bug in the check.
            // The length message when the length is what is wrong; otherwise the number simply is
            // not one this country issues, which is all that can honestly be said without quoting
            // the pattern at a person who did not write it.
            if (!country.AcceptsDigitCount(NumberBox.Text.Count(char.IsDigit)))
            {
                return _localization.Format("OrderEdit.Validate.PhoneDigits",
                    country.DisplayName(_localization),
                    country.ExpectedDigitsText(_localization));
            }

            return _localization.Format("OrderEdit.Validate.PhoneShape",
                country.DisplayName(_localization));
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
            NumberBox.Text = RegroupStored(country, national);
        }
        finally
        {
            _populating = false;
        }

        // The baseline for HasBeenEdited, taken AFTER the regroup: re-punctuating a stored number is
        // this control's doing, not the user's, and must not read as an edit.
        _loadedNumber = FullNumber;
    }

    /// <summary>
    /// Whether the number on screen differs from the one that was loaded into it.
    /// </summary>
    /// <remarks>
    /// This is what decides how strictly the number is judged, and the distinction it draws is
    /// between a number that was ALREADY STORED and one typed just now — not between a new order and
    /// an old one.
    ///
    /// The leniency exists for a number that predates the per-country length rule: refusing it would
    /// mean an order taken last year could not have its status corrected or its balance cleared until
    /// somebody re-typed a phone number they have no way to verify. That argument covers the stored
    /// value and nothing else. A number typed now is typed with the customer standing there, so it is
    /// held to the country's rule whatever order it belongs to — which is the hole this closes: an
    /// existing order used to accept ANY 7-to-15-digit number in any country.
    ///
    /// Country counts as part of the number, because it is: the same digits are a valid Chinese
    /// mobile and an invalid Canadian one, so switching the picker changes the claim being made.
    /// Editing back to the original value reads as unedited, which is correct — the stored number is
    /// what would be saved.
    /// </remarks>
    public bool HasBeenEdited => !string.Equals(FullNumber, _loadedNumber, StringComparison.Ordinal);

    /// <summary>
    /// Whether the number passes the rule that actually applies to it.
    /// </summary>
    /// <remarks>
    /// THE decision, in one place, because every screen collecting a phone number has to make it the
    /// same way. It lived in <c>OrderEditWindow</c> alone, and the custom-made record editor — which
    /// hosts this same control — validated nothing at all, so the rule could be walked around simply
    /// by editing the record instead of the order.
    ///
    /// Strict unless the number is a STORED value nobody has touched. A blank baseline means there is
    /// nothing to grandfather (a new record, or one that never carried a number), and an edit means
    /// somebody is typing it now with the customer there to be asked — both get the country's own
    /// rule. Only a number already in the database, left alone, keeps the loose one, because refusing
    /// that would strand the record until somebody re-typed a number they cannot verify.
    /// </remarks>
    public bool IsAcceptable => _loadedNumber.Length == 0 || HasBeenEdited ? IsValid : IsValidLoose;

    /// <summary>
    /// Re-groups a stored number for display, but only when doing so is unambiguous.
    /// </summary>
    /// <remarks>
    /// A number already in the database is a fact about a customer, so it is re-punctuated only when it
    /// is plainly just a number: nothing but digits and ordinary separators, and a length this country
    /// has an exact grouping for. Anything else — an extension, a note, a number that never fitted the
    /// country in the first place — comes back exactly as it was stored, because re-grouping a value
    /// this control does not understand is how a shop ends up printing a wrong number on a receipt.
    /// </remarks>
    private static string RegroupStored(PhoneCountry country, string national)
    {
        if (national.Length == 0)
            return national;

        if (national.Any(c => !char.IsDigit(c) && !KnownSeparators.Contains(c, StringComparison.Ordinal)))
            return national;

        // An EXACT pattern for that many digits, not merely a length the country accepts. Japan's
        // ten-digit numbers have no pattern, and a stored "03-1234-5678" is a person's own grouping —
        // correct, and not this control's to strip.
        return country.NationalFormats.ContainsKey(national.Count(char.IsDigit))
            ? country.FormatNational(national)
            : national;
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

        // The same digits are written differently in different countries, so switching the picker
        // re-groups what is already typed rather than leaving a Canadian grouping on a Chinese number.
        WriteGrouped(NumberBox.Text, DigitsBefore(NumberBox.Text, NumberBox.Text.Length));
        PhoneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnNumberChanged(object sender, TextChangedEventArgs e)
    {
        // _formatting: WriteGrouped assigns Text, which lands back here. Without the guard the
        // control would re-enter its own formatter on every keystroke.
        if (_populating || _formatting)
            return;

        WriteGrouped(NumberBox.Text, DigitsBefore(NumberBox.Text, CaretAfterEdit(e)));
        PhoneChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Where the caret sits after the edit that raised <paramref name="e"/>.</summary>
    /// <remarks>
    /// Taken from the CHANGE rather than from <c>SelectionStart</c>. Whether the box has already moved
    /// its caret past the inserted text by the time this event fires depends on how the text arrived —
    /// a keystroke, a paste and an assignment to <c>Text</c> do not agree — and reading the selection
    /// makes the control's correctness depend on which of those it was. The offset and the length added
    /// say it exactly, for all three.
    /// </remarks>
    private static int CaretAfterEdit(TextChangedEventArgs e)
    {
        var caret = 0;
        foreach (var change in e.Changes)
            caret = Math.Max(caret, change.Offset + change.AddedLength);

        return caret;
    }

    /// <summary>
    /// Backspace onto punctuation deletes the digit in FRONT of it, not the punctuation.
    /// </summary>
    /// <remarks>
    /// The separators belong to the format, not to the user: deleting one alone would be put straight
    /// back by the re-group that follows, so the key would read as doing nothing and the caret would
    /// sit in the same place. What the user is reaching for is the digit behind the dash.
    /// </remarks>
    private void OnNumberPreviewKeyDown(object sender, KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key != Key.Back || NumberBox.SelectionLength > 0 || NumberBox.IsReadOnly)
            return;

        var caret = NumberBox.SelectionStart;
        var text = NumberBox.Text;
        if (caret == 0 || caret > text.Length || char.IsDigit(text[caret - 1]))
            return;

        var cut = caret;
        while (cut > 0 && !char.IsDigit(text[cut - 1]))
            cut--;

        // Only punctuation behind the caret and nothing else — there is no digit to take.
        if (cut == 0)
            return;

        WriteGrouped(string.Concat(text.AsSpan(0, cut - 1), text.AsSpan(caret)), DigitsBefore(text, cut - 1));
        PhoneChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    /// <summary>
    /// Puts <paramref name="raw"/> in the box, grouped, with the caret
    /// <paramref name="digitsBeforeCaret"/> digits in.
    /// </summary>
    /// <remarks>
    /// The caret is restored by DIGIT position rather than character position because the re-group
    /// inserts and removes separators on both sides of it: "four digits in" is the only landmark that
    /// survives the rewrite, and a caret restored by character index jumps a place every time a dash
    /// appears.
    /// </remarks>
    private void WriteGrouped(string raw, int digitsBeforeCaret)
    {
        var grouped = SelectedCountry?.FormatNational(raw) ?? raw;

        _formatting = true;
        try
        {
            NumberBox.Text = grouped;
            NumberBox.SelectionStart = OffsetAfterDigits(grouped, digitsBeforeCaret);
            NumberBox.SelectionLength = 0;
        }
        finally
        {
            _formatting = false;
        }
    }

    /// <summary>How many digits sit in the first <paramref name="length"/> characters.</summary>
    private static int DigitsBefore(string text, int length)
        => text.Take(Math.Clamp(length, 0, text.Length)).Count(char.IsDigit);

    /// <summary>The offset just past the <paramref name="count"/>-th digit; the end when there are fewer.</summary>
    private static int OffsetAfterDigits(string text, int count)
    {
        if (count <= 0)
            return 0;

        var seen = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i]))
                continue;

            if (++seen == count)
                return i + 1;
        }

        return text.Length;
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

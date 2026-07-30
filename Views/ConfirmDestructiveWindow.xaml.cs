using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using CameywareOrder.Localization;

namespace CameywareOrder.Views;

/// <summary>
/// The gate in front of an irreversible Store Management action: a phrase generated fresh each time,
/// which has to be typed exactly before either button becomes usable.
/// </summary>
/// <remarks>
/// Why a random phrase rather than a Yes/No dialog, or a fixed word: an OK button is clicked by muscle
/// memory, and a fixed word is typed by it once you have done it twice. A string that has to be READ off
/// this window is the cheapest way to guarantee the eye passed over the summary of what is about to be
/// destroyed. It is a speed bump, not a security control — the person is already an administrator.
///
/// The window is deliberately dumb: it verifies typing and reports which button was pressed. It performs
/// nothing. What "proceed" means — delete two shops, or wipe the installation — belongs to the caller,
/// which is also the only thing that can describe the impact accurately.
/// </remarks>
public partial class ConfirmDestructiveWindow : Window
{
    /// <summary>
    /// Glyphs that survive being copied by eye. For each commonly confused pair — O/0, I/1/L, S/5, Z/2,
    /// B/8, G/6, Q/O — NEITHER member is here, so no character in a challenge has a lookalike in it.
    /// </summary>
    /// <remarks>
    /// A challenge somebody fails because the font betrayed them teaches only that confirmations are
    /// broken, which is the opposite of the point.
    ///
    /// The first version excluded S but kept 5, and kept both halves of Z/2, B/8 and G/6, while its
    /// comment claimed the confusable characters were gone. The harness asserting the claim is what
    /// found it — the comment was aspirational and the alphabet was not checked against it.
    ///
    /// 22 characters over 10 positions is ~2.7e13 combinations. This is a speed bump to force the eye
    /// past the impact summary, not a secret, so breadth costs nothing worth having.
    /// </remarks>
    private const string ChallengeAlphabet = "ACDEFHJKMNPRTUVWXY3479";

    private const int ChallengeLength = 10;

    private readonly LocalizationService _localization;
    private readonly string _challenge;

    /// <param name="headline">What is about to happen, in one line.</param>
    /// <param name="impact">
    /// One entry per thing that will be destroyed — a shop and its order count, typically. Shown
    /// verbatim, so the caller decides the wording and the order.
    /// </param>
    /// <param name="proceedLabel">Wording for the destructive button; "remove now" reads differently
    /// from "reinitialize", and a generic label is how the wrong thing gets confirmed.</param>
    /// <param name="offerSaveFirst">
    /// False for actions where saving the records first makes no sense. The button is HIDDEN rather than
    /// disabled in that case: a permanently greyed control reads as something the user failed to unlock.
    /// </param>
    public ConfirmDestructiveWindow(
        LocalizationService localization,
        string headline,
        IReadOnlyList<string> impact,
        string proceedLabel,
        bool offerSaveFirst = true)
    {
        InitializeComponent();

        _localization = localization;
        _challenge = GenerateChallenge();

        HeadlineText.Text = headline;
        SubheadText.Text = _localization["Store.Confirm.Subhead"];
        ImpactHeading.Text = _localization["Store.Confirm.ImpactHeading"];
        ImpactItems.ItemsSource = impact;
        ChallengePrompt.Text = _localization.Format("Store.Confirm.Prompt", ChallengeLength);
        ChallengeText.Text = _challenge;
        ProceedButton.Content = proceedLabel;

        if (!offerSaveFirst)
            SaveFirstButton.Visibility = Visibility.Collapsed;

        EntryBox.Focus();
    }

    /// <summary>What the user chose. Null unless the dialog returned true.</summary>
    public ConfirmedAction? Action { get; private set; }

    /// <summary>
    /// A fresh phrase per dialog, from a cryptographic generator rather than <c>Random</c>. Not because
    /// an attacker is guessing it — they are already signed in as the administrator — but because a
    /// per-process seeded `Random` can repeat a sequence, and a challenge that is the same twice in a row
    /// is one the user can type without reading, which is the entire thing this defends against.
    /// </summary>
    private static string GenerateChallenge()
        => string.Concat(Enumerable.Range(0, ChallengeLength)
            .Select(_ => ChallengeAlphabet[RandomNumberGenerator.GetInt32(ChallengeAlphabet.Length)]));

    /// <summary>
    /// Case-SENSITIVE, and trimmed only at the ends. Accepting any case would halve the attention the
    /// phrase demands, which is the only thing it is for; trimming the ends forgives a stray space from
    /// a double-click rather than a mistyped character.
    /// </summary>
    private void OnEntryChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // Typing means they have moved on from copying, so the button stops claiming a past copy.
        CopyButton.Content = _localization["Store.Confirm.Copy"];

        var typed = EntryBox.Text.Trim();
        var matches = string.Equals(typed, _challenge, StringComparison.Ordinal);

        SaveFirstButton.IsEnabled = matches;
        ProceedButton.IsEnabled = matches;

        if (typed.Length == 0)
        {
            MatchText.Text = string.Empty;
            return;
        }

        MatchText.Text = _localization[matches ? "Store.Confirm.Matched" : "Store.Confirm.NotMatched"];
        MatchText.Foreground = new SolidColorBrush(matches
            ? Color.FromRgb(0x04, 0x78, 0x57)
            : Color.FromRgb(0xB9, 0x1C, 0x1C));
    }

    /// <summary>
    /// Puts the phrase on the clipboard and says so on the button itself.
    /// </summary>
    /// <remarks>
    /// A button rather than leaving the user to select-and-Ctrl+C. Selection worked, but the phrase sat
    /// on a dark panel where the theme's selection highlight was almost invisible, so it read as
    /// uncopyable — and "read as uncopyable" and "is uncopyable" are the same defect from where the user
    /// sits. The confirmation label reverts, so the button does not end up permanently claiming a copy
    /// that happened once.
    ///
    /// Clipboard.SetText can fail: another process may hold the clipboard open. Swallowed rather than
    /// thrown, because failing to copy must not take down a window whose only job is to stop somebody
    /// deleting data by accident.
    /// </remarks>
    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_challenge);
            CopyButton.Content = _localization["Store.Confirm.Copied"];
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            CopyButton.Content = _localization["Store.Confirm.CopyFailed"];
        }
    }

    private void OnSaveFirstClick(object sender, RoutedEventArgs e) => Finish(ConfirmedAction.SaveThenProceed);

    private void OnProceedClick(object sender, RoutedEventArgs e) => Finish(ConfirmedAction.ProceedNow);

    /// <summary>
    /// Re-checks the phrase instead of trusting <c>IsEnabled</c>. A button's enabled state is a view
    /// concern and this is the last gate before something irreversible; the two costs are not comparable.
    /// </summary>
    private void Finish(ConfirmedAction action)
    {
        if (!string.Equals(EntryBox.Text.Trim(), _challenge, StringComparison.Ordinal))
            return;

        Action = action;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}

/// <summary>Which button the administrator pressed once the phrase matched.</summary>
public enum ConfirmedAction
{
    /// <summary>Write the records out to a file the user picks, and only then destroy them.</summary>
    SaveThenProceed = 1,

    /// <summary>Destroy them without keeping a copy.</summary>
    ProceedNow = 2,
}

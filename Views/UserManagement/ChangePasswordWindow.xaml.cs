using System.Windows;
using CameywareOrder.Localization;
using CameywareOrder.Services;

namespace CameywareOrder.Views;

/// <summary>
/// Changing your own password, from inside a live session.
/// </summary>
/// <remarks>
/// The gap this closes: v9.2.0 made the application DEMAND a password change on first sign-in and
/// after an administrative reset, but wanting one — the ordinary case — still meant asking a manager
/// to set it, and a manager who sets your password knows it.
///
/// It goes through <see cref="AuthenticationService.ChangeOwnPassword"/>, which is the only method
/// that clears <c>MustChangePassword</c>, and which proves the CURRENT password rather than trusting
/// the session. Asking for it inside a signed-in window looks redundant and is not: the session
/// proves somebody signed in earlier, not that the person now at the keyboard is that somebody. An
/// unlocked machine at a counter is precisely the case, and without the check it would take one
/// passer-by ten seconds to lock the real owner out of their own account.
///
/// Reporting follows the form conventions in SKILL §4b, scaled to a dialog this small: every problem
/// is named inline under the boxes, in ONE place, and typing clears the message rather than
/// re-validating — a half-typed password must not turn red under the cursor.
/// </remarks>
public partial class ChangePasswordWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly string _userName;

    public ChangePasswordWindow(LocalizationService localization, UserAccount account)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(account);

        _localization = localization;
        _userName = account.UserName;

        // A Window's own properties are set before its Resources exist, so the title cannot be bound
        // to the string table in markup — see context.md. Set here instead.
        Title = localization["Password.Change.Title"];

        // The USER NAME rather than the display label, for the same reason the lock screen shows it:
        // this is the identifier the credential belongs to, and DisplayLabel silently falls back to
        // the login only when the account has no person's name on it.
        AccountText.Text = localization.Format("Password.Change.Subhead", account.UserName);
        RuleText.Text = localization.Format(
            "Password.Change.Rule", AuthenticationService.MinimumPasswordLength);

        Loaded += (_, _) => CurrentPasswordBox.Focus();
    }

    /// <summary>True when the password was actually changed, so the caller can say so.</summary>
    public bool Changed { get; private set; }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var current = CurrentPasswordBox.Password;
        var replacement = NewPasswordBox.Password;

        // Checked here rather than left to the service: "the two boxes disagree" is a fact about this
        // FORM, and the service is handed one password. Everything else — length, not the user name,
        // whether the current one is right — belongs to the service, which is the single definition
        // of the policy (SKILL §1: one rule, one place).
        if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(replacement))
        {
            ShowError("Users.Error.PasswordRequired");
            return;
        }

        if (!string.Equals(replacement, ConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            ShowError("Users.Error.PasswordMismatch");
            return;
        }

        var result = AuthenticationService.Instance.ChangeOwnPassword(_userName, current, replacement);

        if (result != AccountOperationResult.Success)
        {
            // NotFound is what a wrong current password comes back as — the service deliberately does
            // not distinguish "no such account" from "wrong password" — so it is reported as the one
            // thing it can actually mean here: this window was opened for an account that exists.
            ShowError(result switch
            {
                AccountOperationResult.PasswordTooShort => "Users.Error.PasswordTooShort",
                AccountOperationResult.PasswordSameAsUserName => "Users.Error.PasswordSameAsUserName",
                AccountOperationResult.Deactivated => "Login.Deactivated",
                _ => "Password.Change.WrongCurrent"
            });
            return;
        }

        Changed = true;
        Close();
    }

    /// <summary>Typing CLEARS the message; it never re-validates. See the remarks on the class.</summary>
    private void OnPasswordTyped(object sender, RoutedEventArgs e)
        => ErrorText.Visibility = Visibility.Collapsed;

    private void ShowError(string key)
    {
        // The one key that carries an argument. Formatting a key with no placeholder is harmless, but
        // naming it here keeps the call site honest about which message needs the policy value.
        ErrorText.Text = key == "Users.Error.PasswordTooShort"
            ? _localization.Format(key, AuthenticationService.MinimumPasswordLength)
            : _localization[key];

        ErrorText.Visibility = Visibility.Visible;
    }
}

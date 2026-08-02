using System.Windows;
using System.Windows.Controls;
using CameywareOrder.Localization;
using CameywareOrder.Services;

namespace CameywareOrder.Views;

/// <summary>
/// First screen of the application: signs a user in before anything else runs. Replaces the
/// standalone language picker, whose welcome banner it carries over, and which had become
/// redundant once each shop gained its own preferred language.
///
/// Constructed by hand rather than through DI, because it runs before the host is built.
/// </summary>
public partial class LoginWindow : Window
{
    private readonly LocalizationService _localization;
    private bool _isLoadingLanguages;

    // The credential that was accepted but is not allowed to open a session yet. Held so the change
    // step can prove it to ChangeOwnPassword without asking the user to type it a second time —
    // they typed it thirty seconds ago and re-asking reads as a system that did not believe them.
    private string? _pendingUserName;
    private string _pendingPassword = string.Empty;

    public LoginWindow(LocalizationService localization)
    {
        InitializeComponent();
        _localization = localization;

        PopulateLanguages();

        // The user name box starts EMPTY on every path, including a fresh installation's first
        // launch. It used to be seeded with "admin" as a convenience, which meant the login screen
        // announced to anyone who opened the application that an account by that name exists — and
        // that account is the one that can never be deleted, demoted or locked out. OnSignInClick
        // already refuses to say whether a failure was an unknown name or a wrong password,
        // precisely so the screen cannot be used to discover account names; pre-filling one handed
        // that away before the first keystroke.
        //
        // Signing out is also overwhelmingly "somebody else takes over", so the caret starting in
        // an empty name box is the right behaviour there too — which is why there is no longer a
        // parameter distinguishing the two paths.
        Loaded += (_, _) => UserNameBox.Focus();
    }

    /// <summary>The account that signed in, or null when the window was closed without signing in.</summary>
    public UserAccount? SignedInUser { get; private set; }

    private void PopulateLanguages()
    {
        _isLoadingLanguages = true;
        try
        {
            LanguageBox.ItemsSource = _localization.AvailableLanguages;
            LanguageBox.DisplayMemberPath = nameof(LanguageOption.Name);
            LanguageBox.SelectedValuePath = nameof(LanguageOption.Code);
            LanguageBox.SelectedValue = _localization.CurrentLanguageCode;
        }
        finally
        {
            _isLoadingLanguages = false;
        }
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        // Guarded against the initial population, which would otherwise count as a user choice and
        // rewrite the saved preference on every launch.
        if (_isLoadingLanguages || LanguageBox.SelectedValue is not string code)
            return;

        // This is a pre-shop screen, so the choice belongs to the machine-wide preference, which
        // App persists via LanguageChanged. That makes it the value this picker shows next launch,
        // and — for a user allowed to choose — the language the session actually runs in.
        _localization.SetLanguage(code);
    }

    /// <summary>
    /// The single button, which does whichever of the two things the window is currently asking
    /// for. One button rather than two because only one can be <c>IsDefault</c> — WPF hands Enter to
    /// the first one registered in the focus scope, so a hidden second default would quietly steal
    /// the key from the panel actually on screen.
    /// </summary>
    private void OnSignInClick(object sender, RoutedEventArgs e)
    {
        if (_pendingUserName is not null)
            SubmitNewPassword(_pendingUserName);
        else
            SubmitCredential();
    }

    private void SubmitCredential()
    {
        var userName = UserNameBox.Text.Trim();
        var result = AuthenticationService.Instance.Authenticate(userName, PasswordBox.Password);

        if (result.Failure == SignInFailure.PasswordChangeRequired)
        {
            // Not a failure to report. The credential was right; the account simply still carries
            // the password somebody else chose for it, and this is the step that ends that.
            _pendingUserName = userName;
            _pendingPassword = PasswordBox.Password;
            ShowPasswordChangeStep();
            return;
        }

        if (result.User is null)
        {
            // One message for both an unknown user name and a wrong password: saying which would
            // turn this dialog into a way to discover valid account names. A deactivated account is
            // the exception — the credential WAS right, and retyping it will never help; the person
            // needs to be told to talk to their manager.
            ErrorText.Text = _localization[result.Failure == SignInFailure.Deactivated
                ? "Login.Deactivated"
                : "Login.Failed"];
            ErrorText.Visibility = Visibility.Visible;
            PasswordBox.Clear();
            PasswordBox.Focus();
            return;
        }

        SignedInUser = result.User;
        DialogResult = true;
    }

    private void ShowPasswordChangeStep()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        ChangeErrorText.Visibility = Visibility.Collapsed;
        NewPasswordBox.Clear();
        ConfirmPasswordBox.Clear();

        CredentialPanel.Visibility = Visibility.Collapsed;
        PasswordChangePanel.Visibility = Visibility.Visible;
        SignInButton.Content = _localization["Login.SetPassword"];

        NewPasswordBox.Focus();
    }

    /// <summary>
    /// Writes the new password and, on success, signs in with it — the user asked to sign in, and
    /// making them type the credential they have just chosen would be a second gate for no reason.
    /// </summary>
    private void SubmitNewPassword(string userName)
    {
        if (!string.Equals(NewPasswordBox.Password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
        {
            ShowChangeError("Users.Error.PasswordMismatch");
            return;
        }

        var change = AuthenticationService.Instance.ChangeOwnPassword(
            userName, _pendingPassword, NewPasswordBox.Password);

        if (change != AccountOperationResult.Success)
        {
            ShowChangeError(ChangeErrorKey(change));
            return;
        }

        var result = AuthenticationService.Instance.Authenticate(userName, NewPasswordBox.Password);

        if (result.User is null)
        {
            // Cannot happen from here — the password was just written and verified — but a silent
            // no-op on a button is the worst possible answer, so say something and go back to the
            // start rather than leaving the user pressing a button that does nothing.
            ShowChangeError("Login.Failed");
            return;
        }

        SignedInUser = result.User;
        DialogResult = true;
    }

    private static string ChangeErrorKey(AccountOperationResult result) => result switch
    {
        AccountOperationResult.PasswordRequired => "Users.Error.PasswordRequired",
        AccountOperationResult.PasswordTooShort => "Users.Error.PasswordTooShort",
        AccountOperationResult.PasswordSameAsUserName => "Users.Error.PasswordSameAsUserName",
        AccountOperationResult.Deactivated => "Login.Deactivated",
        _ => "Login.Failed"
    };

    private void ShowChangeError(string key)
    {
        ChangeErrorText.Text = _localization.Format(key, AuthenticationService.MinimumPasswordLength);
        ChangeErrorText.Visibility = Visibility.Visible;
    }
}

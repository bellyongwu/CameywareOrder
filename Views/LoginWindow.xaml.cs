using System.Windows;
using System.Windows.Controls;
using LeeYongeOrdering.Localization;
using LeeYongeOrdering.Services;

namespace LeeYongeOrdering.Views;

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

    /// <param name="seedDefaultUserName">
    /// True on the startup path, false when signing back in after a sign-out — see the comment
    /// below on why a returning user gets an empty box.
    /// </param>
    public LoginWindow(LocalizationService localization, bool seedDefaultUserName = true)
    {
        InitializeComponent();
        _localization = localization;

        PopulateLanguages();

        if (seedDefaultUserName)
        {
            // Seeded so the very first sign-in on a new installation needs no guesswork; the
            // password is still typed, so this is a convenience rather than a bypass.
            UserNameBox.Text = "admin";
            Loaded += (_, _) => PasswordBox.Focus();
        }
        else
        {
            // Signing out is overwhelmingly "somebody else takes over", so the name is theirs to
            // enter and the caret starts there.
            Loaded += (_, _) => UserNameBox.Focus();
        }
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

    private void OnSignInClick(object sender, RoutedEventArgs e)
    {
        var user = AuthenticationService.Instance.Authenticate(UserNameBox.Text.Trim(), PasswordBox.Password);

        if (user is null)
        {
            // One message for both an unknown user name and a wrong password: saying which would
            // turn this dialog into a way to discover valid account names.
            ErrorText.Text = _localization["Login.Failed"];
            ErrorText.Visibility = Visibility.Visible;
            PasswordBox.Clear();
            PasswordBox.Focus();
            return;
        }

        SignedInUser = user;
        DialogResult = true;
    }
}

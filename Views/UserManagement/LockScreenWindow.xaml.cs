using System.Windows;
using CameywareOrder.Localization;
using CameywareOrder.Services;

namespace CameywareOrder.Views;

/// <summary>
/// The screen a locked session comes back through: the account is already settled, so it asks only
/// for that account's password and returns to the shop the session was locked from.
/// </summary>
/// <remarks>
/// The security of the whole feature rests on two things this class does.
///
/// First, it authenticates for real — the same <see cref="AuthenticationService.Authenticate"/> the
/// login window uses, against the same stored hash. Nothing here compares a remembered password or
/// trusts a flag; a locked session holds no credential to be trusted.
///
/// Second, it accepts ONLY the account that locked it. A different person's correct password is
/// still refused, because unlocking is resuming somebody else's session — it would inherit their
/// shop, their role and their name on every order saved next. Whoever that is signs out instead,
/// which the second button offers and which starts a proper session of their own.
/// </remarks>
public partial class LockScreenWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly string _userName;

    public LockScreenWindow(LocalizationService localization, UserAccount account, string? shopName)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(account);

        _localization = localization;
        _userName = account.UserName;

        // The USER NAME, not the display label. This field is labelled Login.UserName and sits above
        // a password box: it has to show the exact identifier that credential belongs to — the one
        // the login window asks for — or the screen names one thing and authenticates another.
        // DisplayLabel is PersonName.Label(FirstName, LastName, UserName), so it reads as the
        // person's name whenever the account has one and silently falls back to the login when it
        // does not; that fallback is why it looked right on accounts with no name.
        AccountText.Text = account.UserName;
        ShopText.Text = localization.Format("Session.Lock.Subhead", shopName ?? string.Empty);

        Loaded += (_, _) => PasswordBox.Focus();
    }

    /// <summary>True when the password was accepted and the session should resume.</summary>
    public bool Unlocked { get; private set; }

    /// <summary>True when the person at the machine asked to end the session instead.</summary>
    public bool SignOutRequested { get; private set; }

    private void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        var result = AuthenticationService.Instance.Authenticate(_userName, PasswordBox.Password);

        if (result.User is null)
        {
            // Same single message as the login window for a wrong password, and the same distinct
            // one for an account deactivated while it was locked — that credential IS right and
            // retyping it will never help.
            ShowError(result.Failure == SignInFailure.Deactivated
                ? "Login.Deactivated"
                : "Session.Lock.WrongPassword");
            return;
        }

        // Belt and braces. Authenticate was given the locked account's own name, so this cannot
        // differ — but "the session resumed as somebody else" is the one failure this window must
        // never have, and the check costs nothing.
        if (!string.Equals(result.User.UserName, _userName, StringComparison.OrdinalIgnoreCase))
        {
            AuthenticationService.Instance.SignOut();
            ShowError("Session.Lock.WrongPassword");
            return;
        }

        Unlocked = true;
        Close();
    }

    private void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        SignOutRequested = true;
        Close();
    }

    /// <summary>
    /// Closing the window is not a way past it.
    /// </summary>
    /// <remarks>
    /// A lock that the title-bar X dismisses is not a lock. Every route out of this window is a
    /// decision the caller acts on — unlock, or sign out — so a close with neither is treated as
    /// signing out: the session ends and the login screen appears. That is the safe reading of
    /// "the person walked away", and it is what Alt+F4 and the X both do.
    /// </remarks>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!Unlocked && !SignOutRequested)
            SignOutRequested = true;

        base.OnClosing(e);
    }

    private void ShowError(string key)
    {
        ErrorText.Text = _localization[key];
        ErrorText.Visibility = Visibility.Visible;
        PasswordBox.Clear();
        PasswordBox.Focus();
    }
}

using System.Windows;
using CameywareOrder.Localization;
using CameywareOrder.Services;

namespace CameywareOrder.Views;

/// <summary>What the user chose to do with their session.</summary>
public enum SessionAction
{
    /// <summary>Nothing — stay signed in, on this shop.</summary>
    Stay,

    /// <summary>Keep the session and the shop, ask for the password to come back.</summary>
    Lock,

    /// <summary>End the session and return to sign-in.</summary>
    SignOut
}

/// <summary>
/// Asks whether to lock or sign out. Reached with ESC from the main window, and from the toolbar.
/// </summary>
/// <remarks>
/// A themed window rather than a <c>MessageBox</c>, because the two options are not "yes" and "no" —
/// they are two different things to do, and each needs a sentence saying which. A message box would
/// have to spell that out in a paragraph and then label the buttons Yes/No/Cancel anyway.
/// </remarks>
public partial class SessionActionWindow : Window
{
    public SessionActionWindow(LocalizationService localization, UserAccount? account, string? shopName)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(localization);

        // Names the account and the shop. On a machine a crew shares, "lock" and "sign out" mean
        // very different things depending on whose session is open, and the window should not make
        // anybody remember which.
        SubheadText.Text = localization.Format(
            "Session.Action.Subhead",
            account?.DisplayLabel ?? string.Empty,
            shopName ?? string.Empty);

        Loaded += (_, _) => LockButton.Focus();
    }

    /// <summary>The chosen action; <see cref="SessionAction.Stay"/> unless a choice was made.</summary>
    public SessionAction Action { get; private set; } = SessionAction.Stay;

    private void OnLockClick(object sender, RoutedEventArgs e) => Choose(SessionAction.Lock);

    private void OnSignOutClick(object sender, RoutedEventArgs e) => Choose(SessionAction.SignOut);

    private void OnCancelClick(object sender, RoutedEventArgs e) => Choose(SessionAction.Stay);

    private void Choose(SessionAction action)
    {
        Action = action;

        // Close(), not DialogResult. The answer is Action — no caller reads DialogResult — and
        // assigning it throws outright on a window that was not shown with ShowDialog, which makes
        // the window impossible to drive outside a modal loop and so impossible to assert on.
        // Defaulting to Stay is what makes closing with the X mean the same as "Stay here".
        Close();
    }
}

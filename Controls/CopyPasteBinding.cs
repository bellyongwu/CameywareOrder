using System.Windows;
using System.Windows.Input;
using CameywareOrder.Services;

namespace CameywareOrder.Controls;

/// <summary>
/// A surface whose records can be copied and pasted — the whole of what a screen has to supply to
/// get Ctrl+C / Ctrl+V.
/// </summary>
/// <remarks>
/// Five members, none of which say anything about keyboards: the shortcut handling lives once in
/// <see cref="CopyPasteBinding"/> and a screen only answers what it can copy, whether it can accept
/// what is held, and how to duplicate it. A screen added later declares its own
/// <see cref="ClipboardKind"/> and needs nothing here to change.
/// </remarks>
public interface ICopyPasteSurface
{
    /// <summary>
    /// What kind of record this surface deals in. Paste is offered only while
    /// <see cref="AppClipboard"/> holds this kind, which is what stops orders being pasted into a
    /// list of shops.
    /// </summary>
    string ClipboardKind { get; }

    /// <summary>Whether there is a selection worth copying right now.</summary>
    bool CanCopy { get; }

    /// <summary>The selected records, in the form <see cref="Paste"/> will be handed back.</summary>
    IReadOnlyList<object> CopySelection();

    /// <summary>Whether these records can be duplicated here — kind is already checked.</summary>
    bool CanPaste(IReadOnlyList<object> items);

    /// <summary>
    /// Duplicates the records. Returns immediately: the work is allowed to be asynchronous and to
    /// report through the screen's own status line, exactly as the equivalent toolbar action does.
    /// </summary>
    void Paste(IReadOnlyList<object> items);
}

/// <summary>
/// Binds Ctrl+C and Ctrl+V on one control to an <see cref="ICopyPasteSurface"/>.
/// </summary>
/// <remarks>
/// The point is that the shortcut handling is written ONCE. Before this, a list that wanted a
/// keyboard action grew its own <c>KeyDown</c> switch in its window's code-behind, and the fourth
/// such list is where they start disagreeing about what Ctrl+C means. Attaching this instead makes
/// "this list can be copied" a declaration in the markup:
///
/// <code>
/// &lt;ListView ctrl:CopyPasteBinding.Surface="{Binding RelativeSource={RelativeSource AncestorType=Window}}"/&gt;
/// </code>
///
/// Attached to the LIST rather than to the window on purpose. Ctrl+C inside a search box, a notes
/// field or an editable combo has to keep meaning "copy this text" — those controls carry their own
/// class input bindings and are nearer the focused element, so they answer first; a binding on the
/// window would be reached from anywhere in it and would quietly redefine the key everywhere.
///
/// Both an <see cref="InputBinding"/> and a <see cref="CommandBinding"/> are installed per command.
/// The command binding is what executes and what gates the gesture through <c>CanExecute</c>; the
/// input binding is what guarantees the gesture is translated at THIS element rather than relying on
/// the command's own registered gestures being consulted further up the route. Only one of the two
/// can win — the input event is marked handled by whichever matches first — so there is no double
/// execution.
/// </remarks>
public static class CopyPasteBinding
{
    /// <summary>The surface this control copies for. Setting it installs the shortcuts; clearing it removes them.</summary>
    public static readonly DependencyProperty SurfaceProperty =
        DependencyProperty.RegisterAttached(
            "Surface",
            typeof(ICopyPasteSurface),
            typeof(CopyPasteBinding),
            new PropertyMetadata(null, OnSurfaceChanged));

    public static void SetSurface(DependencyObject element, ICopyPasteSurface? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(SurfaceProperty, value);
    }

    public static ICopyPasteSurface? GetSurface(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (ICopyPasteSurface?)element.GetValue(SurfaceProperty);
    }

    private static void OnSurfaceChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not UIElement element)
            return;

        // Re-assigning the surface must not stack a second pair of bindings on top of the first, or
        // one Ctrl+V would paste twice.
        RemoveInstalled(element);

        if (e.NewValue is not ICopyPasteSurface)
            return;

        Install(element, ApplicationCommands.Copy, Key.C, OnCopyExecuted, OnCanCopy);
        Install(element, ApplicationCommands.Paste, Key.V, OnPasteExecuted, OnCanPaste);
    }

    private static void Install(
        UIElement element, RoutedCommand command, Key key,
        ExecutedRoutedEventHandler executed, CanExecuteRoutedEventHandler canExecute)
    {
        element.CommandBindings.Add(new CommandBinding(command, executed, canExecute));
        element.InputBindings.Add(new KeyBinding(command, key, ModifierKeys.Control));
    }

    /// <summary>
    /// Removes what a previous assignment installed — and only that. Anything the screen registered
    /// for itself is left alone; a behaviour that tears down bindings it did not create is a trap for
    /// whoever adds the next one.
    /// </summary>
    private static void RemoveInstalled(UIElement element)
    {
        foreach (var binding in element.CommandBindings.OfType<CommandBinding>()
                     .Where(binding => IsOurs(binding.Command)).ToList())
        {
            element.CommandBindings.Remove(binding);
        }

        foreach (var binding in element.InputBindings.OfType<KeyBinding>()
                     .Where(binding => IsOurs(binding.Command)).ToList())
        {
            element.InputBindings.Remove(binding);
        }
    }

    private static bool IsOurs(ICommand? command)
        => command == ApplicationCommands.Copy || command == ApplicationCommands.Paste;

    // ── the two commands ──────────────────────────────────────────────────────────────────────────

    private static void OnCanCopy(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = Surface(sender)?.CanCopy == true;
        e.Handled = true;
    }

    private static void OnCopyExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (Surface(sender) is not { } surface || !surface.CanCopy)
            return;

        AppClipboard.Set(surface.ClipboardKind, surface.CopySelection());
        e.Handled = true;
    }

    private static void OnCanPaste(object sender, CanExecuteRoutedEventArgs e)
    {
        var surface = Surface(sender);

        e.CanExecute = surface is not null
            && AppClipboard.Holds(surface.ClipboardKind)
            && surface.CanPaste(AppClipboard.Items);

        e.Handled = true;
    }

    private static void OnPasteExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (Surface(sender) is not { } surface || !AppClipboard.Holds(surface.ClipboardKind))
            return;

        var items = AppClipboard.Items;
        if (!surface.CanPaste(items))
            return;

        e.Handled = true;
        surface.Paste(items);
    }

    private static ICopyPasteSurface? Surface(object sender)
        => sender is DependencyObject element ? GetSurface(element) : null;
}

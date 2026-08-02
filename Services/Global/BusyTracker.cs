using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CameywareOrder.Services;

/// <summary>
/// Whether a screen is in the middle of doing something to the data, and what.
/// </summary>
/// <remarks>
/// The state half of the busy indicator; <c>Controls/BusyOverlay</c> is the visible half. Split so a
/// view model can say it is working without referencing a control, and so a second screen showing
/// progress needs no new state class.
///
/// **Counted, not a bool.** Two operations can overlap — a copy that reloads the list while a refresh
/// triggered by the shop switch is still running — and with a bare flag the first one to finish
/// clears the indicator while the second is still working. The scope returned by <see cref="Begin"/>
/// decrements on dispose, so the overlay lifts when the LAST of them finishes.
///
/// **Not thread-safe, and does not need to be.** Every caller is on the UI thread: an async view-model
/// method resumes on the dispatcher, which is where the counter moves. Making it thread-safe would
/// invite it to be used from a background thread, where raising PropertyChanged would throw anyway.
/// </remarks>
public sealed class BusyTracker : INotifyPropertyChanged
{
    private int _depth;
    private string _message = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Whether anything is in progress.</summary>
    public bool IsBusy => _depth > 0;

    /// <summary>What is in progress — already localized by whoever started it.</summary>
    /// <remarks>
    /// A plain string rather than a key, because a tracker has no business knowing about the string
    /// table: the caller knows both what it is doing and which language to say it in, and a panel
    /// running a language PREVIEW would otherwise get the application's language here.
    /// </remarks>
    public string Message => _message;

    /// <summary>
    /// Marks the start of a piece of work. Dispose the result — <c>using</c> — to mark the end.
    /// </summary>
    /// <remarks>
    /// A scope rather than paired Begin/End calls, so an exception on the way out cannot leave the
    /// screen showing a progress bar for work that stopped. That is the failure this shape exists to
    /// prevent, and it is unrecoverable without restarting the window.
    ///
    /// A nested scope keeps the OUTER message: the first caller described the operation the user
    /// asked for, and the inner ones are steps within it.
    /// </remarks>
    public IDisposable Begin(string message)
    {
        if (_depth == 0)
        {
            _message = message ?? string.Empty;
            OnPropertyChanged(nameof(Message));
        }

        _depth++;

        if (_depth == 1)
            OnPropertyChanged(nameof(IsBusy));

        return new Scope(this);
    }

    private void End()
    {
        if (_depth == 0)
            return;

        _depth--;

        if (_depth > 0)
            return;

        _message = string.Empty;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(Message));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>One piece of work in progress. Idempotent: disposing twice does not double-decrement.</summary>
    private sealed class Scope : IDisposable
    {
        private BusyTracker? _owner;

        public Scope(BusyTracker owner) => _owner = owner;

        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            owner?.End();
        }
    }
}

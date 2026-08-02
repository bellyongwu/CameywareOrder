using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

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
    /// <summary>
    /// The shortest time the indicator stays up once it has appeared.
    /// </summary>
    /// <remarks>
    /// Without a floor, most operations here finish faster than a person can register a change: the
    /// overlay appears and vanishes inside one frame, and the screen reads as having done nothing at
    /// all. Holding it briefly is the difference between "did that work?" and "yes, it saved".
    ///
    /// A QUARTER SECOND, not longer. This is a floor on perception, not an artificial delay — long
    /// enough to be seen, short enough that somebody deleting twenty orders in a row never waits on
    /// it. The work itself is never held up: the data is already written by the time this runs, and
    /// only the indicator lingers.
    /// </remarks>
    public static readonly TimeSpan MinimumVisible = TimeSpan.FromMilliseconds(250);

    private int _depth;
    private string _message = string.Empty;
    private DateTime _shownAtUtc;
    private DispatcherTimer? _holdTimer;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Whether the indicator should be showing.
    /// </summary>
    /// <remarks>
    /// True while work is in progress AND during the <see cref="MinimumVisible"/> hold that follows a
    /// very fast one. The hold is the whole point of the floor: with this reading <c>_depth > 0</c>
    /// alone, the last scope's dispose would hide the overlay instantly and the timer would have
    /// nothing left to keep up.
    /// </remarks>
    public bool IsBusy => _depth > 0 || _holdTimer is not null;

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
            // A hold left over from an operation that finished moments ago: cancel it and reuse the
            // indicator that is still on screen, rather than letting the old timer hide it in the
            // middle of the new work.
            StopHold();

            _message = message ?? string.Empty;
            _shownAtUtc = DateTime.UtcNow;
            OnPropertyChanged(nameof(Message));
        }

        _depth++;

        if (_depth == 1)
            OnPropertyChanged(nameof(IsBusy));

        return new Scope(this);
    }

    /// <summary>
    /// Ends one piece of work, holding the indicator up to <see cref="MinimumVisible"/> if the whole
    /// operation was faster than the eye.
    /// </summary>
    /// <remarks>
    /// The hold is on the INDICATOR only. Everything the operation did is already done and the list
    /// is already redrawn behind the scrim by the time this runs — nothing waits on the timer, and a
    /// caller that starts more work during the hold cancels it (see <see cref="Begin"/>).
    /// </remarks>
    private void End()
    {
        if (_depth == 0)
            return;

        _depth--;

        if (_depth > 0)
            return;

        var shownFor = DateTime.UtcNow - _shownAtUtc;

        // A clock that moved backwards would make `shownFor` negative and hold the indicator for the
        // full window every time; clamping at zero costs nothing and cannot misbehave.
        var remaining = MinimumVisible - (shownFor < TimeSpan.Zero ? TimeSpan.Zero : shownFor);

        if (remaining <= TimeSpan.Zero)
        {
            Clear();
            return;
        }

        _holdTimer = new DispatcherTimer { Interval = remaining };
        _holdTimer.Tick += (_, _) =>
        {
            StopHold();

            // Re-checked: work may have started again while the timer was pending, in which case the
            // indicator belongs to that work now and must not be torn down.
            if (_depth == 0)
                Clear();
        };
        _holdTimer.Start();
    }

    private void StopHold()
    {
        _holdTimer?.Stop();
        _holdTimer = null;
    }

    private void Clear()
    {
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

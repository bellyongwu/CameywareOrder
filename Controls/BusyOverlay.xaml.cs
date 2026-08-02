using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CameywareOrder.Services;

namespace CameywareOrder.Controls;

/// <summary>
/// Shows that data is being read or written, over whatever it is placed on.
/// </summary>
/// <remarks>
/// The reusable half of the busy indicator: point it at a <see cref="BusyTracker"/> and it appears
/// while that tracker is working. Nothing here knows what the work IS — refreshing, saving, copying,
/// deleting — which is what lets one control serve every screen.
///
/// It subscribes to the tracker, so a window that swaps trackers or closes must not leave it
/// attached; <see cref="OnTrackerChanged"/> detaches the previous one, and Unloaded detaches the
/// current. That is the same leak <c>MainViewModel.Detach</c> and <c>LocalizationScope.Detach</c>
/// exist for — a long-lived observable holding a control alive.
/// </remarks>
public partial class BusyOverlay : UserControl
{
    /// <summary>The tracker whose state this overlay shows.</summary>
    public static readonly DependencyProperty TrackerProperty =
        DependencyProperty.Register(
            nameof(Tracker),
            typeof(BusyTracker),
            typeof(BusyOverlay),
            new PropertyMetadata(null, OnTrackerChanged));

    public BusyOverlay()
    {
        InitializeComponent();
        Unloaded += (_, _) => Detach(Tracker);
    }

    public BusyTracker? Tracker
    {
        get => (BusyTracker?)GetValue(TrackerProperty);
        set => SetValue(TrackerProperty, value);
    }

    private static void OnTrackerChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not BusyOverlay overlay)
            return;

        overlay.Detach(e.OldValue as BusyTracker);

        if (e.NewValue is BusyTracker tracker)
            tracker.PropertyChanged += overlay.OnTrackerPropertyChanged;

        overlay.Refresh();
    }

    private void Detach(BusyTracker? tracker)
    {
        if (tracker is not null)
            tracker.PropertyChanged -= OnTrackerPropertyChanged;
    }

    private void OnTrackerPropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    /// <summary>
    /// Reflects the tracker's state.
    /// </summary>
    /// <remarks>
    /// Visibility is ASSIGNED here rather than bound. The overlay's own <c>Visibility</c> is the one
    /// property a host might also want to control, and a local assignment permanently replaces a
    /// binding on the same property — the trap <c>PanelTransition</c> documents. One writer, and it
    /// is this method.
    /// </remarks>
    private void Refresh()
    {
        var busy = Tracker?.IsBusy == true;

        Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        MessageText.Text = Tracker?.Message ?? string.Empty;

        // The animation is stopped while hidden. An indeterminate ProgressBar animates whether or not
        // anybody can see it, and this control sits on the busiest window in the application.
        Bar.IsIndeterminate = busy;
    }
}

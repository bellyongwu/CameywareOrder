using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CameywareOrder.Animations;

/// <summary>How a panel should animate as it opens and closes.</summary>
public enum PanelTransitionMode
{
    /// <summary>No animation — the panel appears and disappears instantly.</summary>
    None = 0,

    /// <summary>Fade only.</summary>
    Fade = 1,

    /// <summary>Fade with a short vertical slide, which reads as the panel arriving rather than blinking on.</summary>
    FadeSlide = 2
}

/// <summary>
/// The application's single open/close transition for panels: half a second, cubic ease-in-out, and
/// a short slide. Opt a panel in with <c>anim:PanelTransition.Mode="FadeSlide"</c>; the timing and
/// the curve live here so "make the app feel slower/faster" is one edit rather than a hunt through
/// storyboards.
/// </summary>
/// <remarks>
/// TWO THINGS MAKE THIS SAFE TO PUT ON AN ARBITRARY PANEL, and both are easy to get wrong:
///
/// 1. <b>It never assigns <see cref="UIElement.Visibility"/> directly.</b> A local assignment would
///    permanently replace any <c>{Binding}</c> on that property, and several panels in this app are
///    bound (the order-detail sections go through <c>BooleanToVisibility</c>). Instead the closing
///    half animates Visibility with a key-frame track inside the storyboard: an animation outranks
///    the binding while it runs and hands the property straight back when it stops, so the binding
///    survives untouched.
///
/// 2. <b>The close animation re-shows the panel, so it re-enters.</b> By the time WPF tells us the
///    panel became invisible it is already gone; the storyboard has to put it back at t=0 to have
///    something to fade. That raises IsVisibleChanged again, which would start an opening animation
///    on a closing panel — hence <see cref="IsAnimatingProperty"/>, cleared one dispatcher turn AFTER
///    the storyboard finishes so the property reverting to its real value is suppressed too.
/// </remarks>
public static class PanelTransition
{
    /// <summary>The global duration. Change it here and every panel follows.</summary>
    private static readonly Duration TransitionDuration = new(TimeSpan.FromSeconds(0.5));

    /// <summary>
    /// The global curve: accelerate out of rest, decelerate into it. Deliberately NOT linear — a
    /// linear fade reads as a mechanical wipe, while ease-in-out reads as the panel moving.
    /// </summary>
    private static readonly IEasingFunction TransitionEase =
        new CubicEase { EasingMode = EasingMode.EaseInOut };

    /// <summary>How far a sliding panel travels, in device-independent pixels.</summary>
    private const double SlideDistance = 10;

    /// <summary>
    /// A storyboard target path. Wrapped so the overload is chosen explicitly: a one-argument
    /// <c>new PropertyPath(x)</c> sits between <c>PropertyPath(object)</c> and
    /// <c>PropertyPath(string, params object[])</c>, and the reader cannot tell which one they got.
    /// Passing the empty parameter array names the string overload at every call site.
    /// </summary>
    private static PropertyPath TargetPath(string path) => new(path, Array.Empty<object>());

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.RegisterAttached(
            "Mode",
            typeof(PanelTransitionMode),
            typeof(PanelTransition),
            new PropertyMetadata(PanelTransitionMode.None, OnModeChanged));

    /// <summary>Guards the re-entrancy described in the class remarks.</summary>
    private static readonly DependencyProperty IsAnimatingProperty =
        DependencyProperty.RegisterAttached(
            "IsAnimating", typeof(bool), typeof(PanelTransition), new PropertyMetadata(false));

    public static void SetMode(DependencyObject element, PanelTransitionMode value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ModeProperty, value);
    }

    public static PanelTransitionMode GetMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (PanelTransitionMode)element.GetValue(ModeProperty);
    }

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        element.IsVisibleChanged -= OnIsVisibleChanged;

        if ((PanelTransitionMode)e.NewValue != PanelTransitionMode.None)
            element.IsVisibleChanged += OnIsVisibleChanged;
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        // Re-entrancy from our own Visibility track, or from a panel that has not been laid out yet.
        // Without the IsLoaded test every window would play its whole set of panels on first show.
        if ((bool)element.GetValue(IsAnimatingProperty) || !element.IsLoaded)
            return;

        if ((bool)e.NewValue)
            Play(element, opening: true, closedVisibility: Visibility.Collapsed);
        else
            Play(element, opening: false, closedVisibility: element.Visibility);
    }

    private static void Play(FrameworkElement element, bool opening, Visibility closedVisibility)
    {
        var mode = GetMode(element);
        var storyboard = new Storyboard();

        var fade = new DoubleAnimation
        {
            From = opening ? 0 : 1,
            To = opening ? 1 : 0,
            Duration = TransitionDuration,
            EasingFunction = TransitionEase,
            // Stop, not HoldEnd: the element's own Opacity must come back afterwards, or a panel
            // that faded out would still be transparent the next time it is shown.
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(fade, element);
        // Property PATHS rather than DependencyProperty overloads: `new PropertyPath(SomeProperty)`
        // binds to PropertyPath(object) while the string form is the one XAML storyboards use, and
        // the two are easy to confuse at a glance.
        Storyboard.SetTargetProperty(fade, TargetPath("Opacity"));
        storyboard.Children.Add(fade);

        AddSlide(storyboard, element, mode, opening);

        if (!opening)
            AddClosingVisibilityTrack(storyboard, element, closedVisibility);

        element.SetValue(IsAnimatingProperty, true);
        storyboard.Completed += (_, _) => ReleaseAfterCompletion(element);
        storyboard.Begin();
    }

    private static void AddSlide(
        Storyboard storyboard, FrameworkElement element, PanelTransitionMode mode, bool opening)
    {
        if (mode != PanelTransitionMode.FadeSlide)
            return;

        // Only when the panel has no transform of its own — replacing one would silently undo
        // whatever layout it was doing.
        if (element.RenderTransform is not null && !element.RenderTransform.Value.IsIdentity)
            return;

        if (element.RenderTransform is not TranslateTransform)
            element.RenderTransform = new TranslateTransform();

        var slide = new DoubleAnimation
        {
            From = opening ? -SlideDistance : 0,
            To = opening ? 0 : -SlideDistance,
            Duration = TransitionDuration,
            EasingFunction = TransitionEase,
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(slide, element);
        Storyboard.SetTargetProperty(slide,
            TargetPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
        storyboard.Children.Add(slide);
    }

    /// <summary>
    /// Holds the panel on screen for the length of the fade and then lets it go. Key frames rather
    /// than an assignment, so a bound Visibility is borrowed rather than overwritten.
    /// </summary>
    private static void AddClosingVisibilityTrack(
        Storyboard storyboard, FrameworkElement element, Visibility closedVisibility)
    {
        var track = new ObjectAnimationUsingKeyFrames
        {
            Duration = TransitionDuration,
            FillBehavior = FillBehavior.Stop
        };
        track.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Visible, KeyTime.FromPercent(0)));
        track.KeyFrames.Add(new DiscreteObjectKeyFrame(closedVisibility, KeyTime.FromPercent(1)));

        Storyboard.SetTarget(track, element);
        Storyboard.SetTargetProperty(track, TargetPath("Visibility"));
        storyboard.Children.Add(track);
    }

    /// <summary>
    /// Clears the guard one dispatcher turn late. The animation hands Visibility back at the moment
    /// Completed fires, and that hand-back raises IsVisibleChanged one more time — clearing the flag
    /// synchronously here would let that final event start an animation of its own.
    /// </summary>
    private static void ReleaseAfterCompletion(FrameworkElement element)
        => element.Dispatcher.BeginInvoke(
            new Action(() => element.SetValue(IsAnimatingProperty, false)),
            System.Windows.Threading.DispatcherPriority.Loaded);
}

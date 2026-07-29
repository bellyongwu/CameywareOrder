using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CameywareOrder.Controls;

/// <summary>
/// Fits every window to the screen it opens on, scaling the whole layout down proportionally when
/// the screen is smaller than the window was drawn for.
/// </summary>
/// <remarks>
/// The bug this exists for: <c>OrderEditWindow</c> declared <c>MinHeight="900"</c> while a common
/// business laptop offers a 1280x752 work area. A window minimum is a FLOOR, not a preference — WPF
/// honours it against the screen — so the bottom 148px sat below the desktop and could not be
/// dragged into view. That band is the pinned Cancel/Save footer, which made saving an order
/// impossible rather than merely awkward. Six other windows opened taller than such a screen without
/// being unusable, because their minimums were small enough to resize down.
///
/// Registered ONCE as a class handler rather than called from each window's constructor. Every
/// window is covered by construction, including any added later — the failure mode of the
/// per-window approach is a new dialog that silently opts out, which is exactly how this shipped in
/// the first place.
///
/// <para><b>Why scale rather than just clamp the minimum.</b> Clamping alone would fit the window
/// and leave the layout cramped — the same controls in less room. Scaling keeps the design's
/// proportions and reduces everything together, which is what "works on a smaller screen" should
/// mean. The scale is computed from the declared MINIMUM, not the design size: a minimum is the
/// author's statement of "below this the layout breaks", and content beyond it is already handled by
/// the ScrollViewer these windows put in their star row.</para>
///
/// <para><b>Never scales up.</b> A large monitor gets the design size, not inflated chrome — WPF is
/// already DPI-aware, so a high-DPI screen is handled before this code sees it, and the records list
/// has its own font-size slider for readability.</para>
///
/// <para><b>Known boundary:</b> the fit is computed when the window opens, on the monitor it opens
/// on. Dragging a window to a smaller second monitor afterwards does not re-fit it. Re-fitting on
/// the move would resize a window under the hand that is dragging it, which is worse than the
/// problem.</para>
/// </remarks>
public static class WindowFitting
{
    /// <summary>
    /// Floor on the scale, so shrinking can never render the UI unreadable. A backstop rather than
    /// an expected path: the smallest screens this app is used on land near 0.62.
    /// </summary>
    private const double MinimumScale = 0.5;

    /// <summary>Below this there is nothing worth scaling, and rounding noise would flicker.</summary>
    private const double NegligibleScaleChange = 0.01;

    private static readonly DependencyProperty FittedProperty =
        DependencyProperty.RegisterAttached(
            "Fitted", typeof(bool), typeof(WindowFitting), new PropertyMetadata(false));

    /// <summary>
    /// Hooks every <see cref="Window"/> in the application. Call once, before any window is shown.
    /// </summary>
    public static void Register()
        => EventManager.RegisterClassHandler(
            typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded fires again whenever a window is hidden and re-shown, and the transform applied the
        // first time is still in place — re-running would compound it and shrink the window a second
        // time.
        if (sender is not Window window || (bool)window.GetValue(FittedProperty))
            return;

        window.SetValue(FittedProperty, true);
        Fit(window);
    }

    /// <summary>
    /// Scales <paramref name="window"/> down to the screen it is on if it needs it, and pulls it
    /// fully into the work area. Returns the scale applied — 1 when the window already fits.
    /// </summary>
    public static double Fit(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return Fit(window, WorkAreaFor(window));
    }

    /// <summary>
    /// As <see cref="Fit(Window)"/>, but into a work area the caller supplies rather than the one
    /// the window happens to be on.
    /// </summary>
    /// <remarks>
    /// Public because "fit this window into this rectangle" is a coherent operation in its own
    /// right, and because the alternative makes the rule untestable: reading the monitor internally
    /// means the result depends on whatever display the machine has today. A harness written on a
    /// 1280x752 laptop passed, then broke on a 2057x1323 desktop — not because the fitting was
    /// wrong, but because nothing was small enough to fit. The screen is an input; this overload is
    /// where it is supplied.
    /// </remarks>
    public static double Fit(Window window, Rect workArea)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Content is not FrameworkElement root || root.ActualHeight <= 0)
            return 1d;

        var work = workArea;
        if (work.Width <= 0 || work.Height <= 0)
            return 1d;

        // The frame and caption sit OUTSIDE the content and do not scale, so they have to come off
        // both sides of the comparison. Measured from the window rather than taken from
        // SystemParameters: it is exact, costs nothing here, and stays right under a custom chrome.
        var chromeWidth = Math.Max(0d, window.ActualWidth - root.ActualWidth);
        var chromeHeight = Math.Max(0d, window.ActualHeight - root.ActualHeight);

        var scale = Math.Min(
            ContentScale(window.MinWidth, chromeWidth, work.Width),
            ContentScale(window.MinHeight, chromeHeight, work.Height));

        scale = Math.Clamp(scale, MinimumScale, 1d);

        if (scale < 1d - NegligibleScaleChange)
            ApplyScale(root, scale);

        ResizeToFit(window, work, chromeWidth, chromeHeight, scale);
        MoveFullyOnScreen(window, work);

        return scale;
    }

    /// <summary>
    /// How far the scalable part of a window has to shrink for <paramref name="required"/> to fit in
    /// <paramref name="available"/>. Chrome is excluded from both, since it is a fixed cost.
    /// </summary>
    private static double ContentScale(double required, double chrome, double available)
    {
        var scalable = required - chrome;
        if (double.IsNaN(scalable) || scalable <= 0d)
            return 1d;

        var room = available - chrome;
        return room <= 0d ? MinimumScale : Math.Min(1d, room / scalable);
    }

    private static void ApplyScale(FrameworkElement root, double scale)
    {
        // Refuse to stack on a transform the window set for itself rather than silently multiplying
        // the two — an unexplained double shrink is far harder to diagnose than no shrink at all.
        if (root.LayoutTransform is not null && !root.LayoutTransform.Value.IsIdentity)
            return;

        // LayoutTransform, NOT RenderTransform: layout has to be MEASURED at the reduced size, or
        // the window still believes it needs its full height and the minimum never comes down.
        var transform = new ScaleTransform(scale, scale);
        transform.Freeze();
        root.LayoutTransform = transform;
    }

    private static void ResizeToFit(Window window, Rect work, double chromeWidth, double chromeHeight, double scale)
    {
        // The minimum has to come down BEFORE the size, or the assignment below is clamped straight
        // back up to the value that did not fit.
        window.MinWidth = ScaledOuter(window.MinWidth, chromeWidth, scale, work.Width);
        window.MinHeight = ScaledOuter(window.MinHeight, chromeHeight, scale, work.Height);

        // A window that sizes itself to its content owns its own dimensions; assigning Width or
        // Height here would override that and pin it at whatever it happened to measure.
        if (window.SizeToContent != SizeToContent.Manual || window.WindowState != WindowState.Normal)
            return;

        window.Width = ScaledOuter(window.Width, chromeWidth, scale, work.Width);
        window.Height = ScaledOuter(window.Height, chromeHeight, scale, work.Height);
    }

    /// <summary>
    /// Scales the content part of an outer dimension and caps the result at the work area. The cap
    /// is what saves the case where <see cref="MinimumScale"/> stopped the shrink short: the window
    /// is then smaller than its own stated minimum, which these layouts survive because the star row
    /// holding the ScrollViewer absorbs it while the pinned title and footer stay put.
    /// </summary>
    private static double ScaledOuter(double outer, double chrome, double scale, double available)
    {
        if (double.IsNaN(outer) || outer <= 0d)
            return outer;

        var scaled = chrome + Math.Max(0d, outer - chrome) * scale;
        return Math.Min(scaled, available);
    }

    private static void MoveFullyOnScreen(Window window, Rect work)
    {
        // CenterOwner ran before this, against the size the window had BEFORE it was fitted, so a
        // shrunk window is left off-centre and can still hang past an edge.
        if (window.WindowState != WindowState.Normal
            || double.IsNaN(window.Left) || double.IsNaN(window.Top))
        {
            return;
        }

        window.Left = Clamp(window.Left, work.Left, work.Right - window.ActualWidth);
        window.Top = Clamp(window.Top, work.Top, work.Bottom - window.ActualHeight);
    }

    /// <summary>
    /// Math.Clamp with the low end winning. A window wider than the work area gives max &lt; min, and
    /// Math.Clamp throws on that — here the sane answer is to align it to the top-left and let the
    /// far edge overflow, because the near edge is the one carrying the title bar.
    /// </summary>
    private static double Clamp(double value, double min, double max)
        => max <= min ? min : Math.Clamp(value, min, max);

    /// <summary>
    /// The work area of the monitor this window is on — not the primary — so a dialog opened on a
    /// smaller second screen is fitted to THAT screen. Falls back to the primary work area when the
    /// window has no handle yet or the call fails.
    /// </summary>
    private static Rect WorkAreaFor(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !TryGetMonitorWorkArea(handle, out var device))
            return SystemParameters.WorkArea;

        // The native rectangle is in PHYSICAL pixels while every WPF dimension here is device
        // independent. On a 150% display the two differ by half again, so comparing them raw would
        // conclude that everything fits and change nothing.
        var source = PresentationSource.FromVisual(window);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        var topLeft = fromDevice.Transform(new Point(device.Left, device.Top));
        var bottomRight = fromDevice.Transform(new Point(device.Right, device.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private static bool TryGetMonitorWorkArea(IntPtr window, out NativeRect work)
    {
        work = default;

        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return false;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return false;

        work = info.Work;
        return true;
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Mirrors the native MONITORINFO. Every field has to be declared in this order even though only
    /// <see cref="Work"/> is read — the layout is a contract with user32, and dropping the fields
    /// that happen to be unused here would silently shift <see cref="Work"/> onto the wrong bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}

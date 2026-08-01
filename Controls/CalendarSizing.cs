using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace CameywareOrder.Controls;

/// <summary>
/// Makes a <see cref="DatePicker"/>'s drop-down calendar at least as wide as the box it belongs to,
/// so the panel lines up with the field instead of hanging off it at the stock ~179px.
/// </summary>
/// <remarks>
/// A FLOOR, not a fixed width. It was an exact <c>Width</c> until the day cells were sized up: the
/// month grid inside a Calendar is content-sized and centred, it does not stretch to fill the panel,
/// so a hard width narrower than the grid needs simply CLIPS the days — which would have hit the
/// narrow pickers in Store Members while the wide one in the order editor looked correct.
///
/// This cannot be done from the theme with a binding, which is the obvious first attempt: the
/// Calendar is created in code by <see cref="DatePicker"/> and lives inside a <see cref="Popup"/>,
/// which is a SEPARATE visual tree. A <c>RelativeSource AncestorType=DatePicker</c> from the Calendar
/// therefore finds nothing — and, worse, finds it SILENTLY: the binding reports no error, the width
/// simply never applies. It measured 179 against a 287-wide picker.
///
/// The width is applied on Loaded and again on every size change, both of which happen BEFORE the
/// drop-down is first opened — <c>DatePicker</c> builds its Calendar in its constructor and attaches
/// it to the popup in OnApplyTemplate. Doing it in CalendarOpened instead would show one frame at
/// the wrong width every time the picker is used.
/// </remarks>
public static class CalendarSizing
{
    public static readonly DependencyProperty MatchOwnerWidthProperty =
        DependencyProperty.RegisterAttached(
            "MatchOwnerWidth",
            typeof(bool),
            typeof(CalendarSizing),
            new PropertyMetadata(false, OnMatchOwnerWidthChanged));

    public static void SetMatchOwnerWidth(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(MatchOwnerWidthProperty, value);
    }

    public static bool GetMatchOwnerWidth(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(MatchOwnerWidthProperty);
    }

    private static void OnMatchOwnerWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DatePicker picker)
            return;

        picker.Loaded -= OnPickerChanged;
        picker.SizeChanged -= OnPickerChanged;

        if ((bool)e.NewValue)
        {
            picker.Loaded += OnPickerChanged;
            picker.SizeChanged += OnPickerChanged;
        }
    }

    private static void OnPickerChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker picker || picker.ActualWidth <= 0)
            return;

        if (picker.Template?.FindName("PART_Popup", picker) is Popup { Child: FrameworkElement panel })
            panel.MinWidth = picker.ActualWidth;
    }
}

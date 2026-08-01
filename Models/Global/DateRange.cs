using System.Globalization;
using CameywareOrder.Localization;

namespace CameywareOrder.Models;

/// <summary>Which calendar period a <see cref="DateRange"/> was built from.</summary>
public enum DatePeriodKind
{
    Day,
    Month,
    Year,
    Custom
}

/// <summary>
/// A period of days, in the shop's own timezone: where a report starts, where it stops, and what
/// kind of period it is.
/// </summary>
/// <remarks>
/// Deliberately general and free of anything about settlement, orders or money — it is a calendar
/// period, and the next thing that needs one (an export, a filter, a second report) should take this
/// rather than inventing a start/end pair of its own.
///
/// <b>Half-open, and LOCAL.</b> <see cref="Start"/> is inclusive and <see cref="EndExclusive"/> is
/// not, which is the shape that gets month boundaries right without anybody writing
/// <c>AddMonths(1).AddDays(-1)</c> and meeting February. Both are local midnights, because "August"
/// means the shop's August; <see cref="Contains"/> takes a UTC instant and converts, so callers hand
/// it a stored <c>OrderDate</c> unchanged.
/// </remarks>
public readonly record struct DateRange(DateTime Start, DateTime EndExclusive, DatePeriodKind Kind)
{
    /// <summary>The last day IN the range — what a heading should say, never the exclusive end.</summary>
    public DateTime LastDay => EndExclusive.AddDays(-1);

    /// <summary>How many days the period covers.</summary>
    public int DayCount => (int)(EndExclusive - Start).TotalDays;

    /// <summary>One day.</summary>
    public static DateRange Day(DateTime anyMoment)
    {
        var start = anyMoment.Date;
        return new DateRange(start, start.AddDays(1), DatePeriodKind.Day);
    }

    /// <summary>The whole calendar month containing <paramref name="anyMoment"/> — the 1st to the last.</summary>
    /// <remarks>
    /// <c>DateTimeKind.Local</c> is stated rather than left to the default: every boundary here is a
    /// local midnight (see the class remarks), and an Unspecified one would be read as UTC by
    /// <c>ToLocalTime</c> the moment it met one.
    /// </remarks>
    public static DateRange Month(DateTime anyMoment)
    {
        var start = new DateTime(anyMoment.Year, anyMoment.Month, 1, 0, 0, 0, DateTimeKind.Local);
        return new DateRange(start, start.AddMonths(1), DatePeriodKind.Month);
    }

    /// <summary>The whole calendar year containing <paramref name="anyMoment"/>.</summary>
    /// <inheritdoc cref="Month" path="/remarks"/>
    public static DateRange Year(DateTime anyMoment)
    {
        var start = new DateTime(anyMoment.Year, 1, 1, 0, 0, 0, DateTimeKind.Local);
        return new DateRange(start, start.AddYears(1), DatePeriodKind.Year);
    }

    /// <summary>
    /// An arbitrary span, both ends INCLUSIVE — which is how a person reading two date pickers means
    /// it, and the one place the half-open end is translated for them.
    /// </summary>
    public static DateRange Custom(DateTime firstDay, DateTime lastDay)
    {
        var start = firstDay.Date;
        var end = lastDay.Date;
        if (end < start)
            (start, end) = (end, start);

        return new DateRange(start, end.AddDays(1), DatePeriodKind.Custom);
    }

    /// <summary>This month — the settlement period a shop wants nine times out of ten.</summary>
    public static DateRange CurrentMonth() => Month(DateTime.Today);

    /// <summary>Whether a stored UTC instant falls inside this local period.</summary>
    public bool Contains(DateTime utcInstant)
    {
        var local = utcInstant.ToLocalTime();
        return local >= Start && local < EndExclusive;
    }

    /// <summary>The period shifted by whole periods of its own kind — the arrows on a report header.</summary>
    /// <remarks>
    /// A custom range steps by its own LENGTH, which is the only reading of "the previous one" that
    /// does not silently change what the user asked for.
    /// </remarks>
    public DateRange Shift(int periods) => Kind switch
    {
        DatePeriodKind.Day => Day(Start.AddDays(periods)),
        DatePeriodKind.Month => Month(Start.AddMonths(periods)),
        DatePeriodKind.Year => Year(Start.AddYears(periods)),
        _ => Custom(Start.AddDays(DayCount * periods), LastDay.AddDays(DayCount * periods))
    };

    /// <summary>
    /// What to print at the top of a report: "August 2026", "2026", a single date, or a span.
    /// </summary>
    /// <param name="text">
    /// Where the format strings come from — an <see cref="ILocalizedText"/> so a report rendered in a
    /// language the application is not currently in gets that language's wording.
    /// </param>
    /// <param name="culture">
    /// Whose month NAMES to use. Passed rather than read from the thread, because a report can be
    /// produced in a language the application is not running in, and .NET already knows every
    /// language's month names — there is no reason to put twelve of them in the string table.
    /// </param>
    /// <remarks>
    /// <c>Period.Month</c> takes the year and the month NAME (not its number), so each language
    /// orders them its own way: "August 2026" against a year-first form. Dates in a span stay ISO,
    /// which is unambiguous in every language and sorts.
    /// </remarks>
    public string Title(ILocalizedText text, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var format = culture ?? CultureInfo.CurrentUICulture;

        return Kind switch
        {
            DatePeriodKind.Day => Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DatePeriodKind.Month => text.Format(
                "Period.Month", Start.Year, format.DateTimeFormat.GetMonthName(Start.Month)),
            DatePeriodKind.Year => text.Format("Period.Year", Start.Year),
            _ => text.Format(
                "Period.Span",
                Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                LastDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        };
    }
}

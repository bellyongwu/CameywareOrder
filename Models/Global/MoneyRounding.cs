namespace CameywareOrder.Models;

/// <summary>
/// How a calculated amount becomes money: two decimal places, and a half rounds UP.
/// </summary>
/// <remarks>
/// Only DERIVED amounts need this — a tax, a total, anything divided or multiplied by a rate. An
/// amount a person typed is already money and is not re-rounded.
///
/// <b>Half goes up</b>, not to even. .NET's <c>decimal.Round</c> defaults to banker's rounding, which
/// turns 89.425 into 89.42; a till that quotes 89.42 on a figure the customer can compute as 89.43 is
/// arguing with its own receipt. The whole codebase now rounds one way, and it is the way shops and
/// tax authorities write it.
///
/// <b>Round the PARTS, then add them.</b> Rounding only the total lets a section print three lines
/// that visibly do not sum to the figure beneath them — each line is shown, so each line is what has
/// to be exact. It costs at most a cent of drift against the unrounded ideal and buys arithmetic a
/// customer can check by hand, which is the trade every point-of-sale system makes.
///
/// This became visible when tax rates gained a third decimal (see <see cref="TaxRateFormat"/>):
/// Quebec's 14.975% lands on a half-cent far more often than a two-decimal rate ever did, so what had
/// been a rare fraction became an everyday one.
/// </remarks>
public static class MoneyRounding
{
    /// <summary>Decimal places money is kept to.</summary>
    public const int Decimals = 2;

    /// <summary>A calculated amount as money: 89.425 becomes 89.43, and −89.425 becomes −89.43.</summary>
    /// <remarks>
    /// <c>AwayFromZero</c> rather than <c>ToPositiveInfinity</c>, so a refund of 89.425 is 89.43 back
    /// to the customer rather than 89.42 — the magnitude rounds up in both directions, which is what
    /// "round a half up" means to the person being charged or repaid.
    /// </remarks>
    public static decimal Round(decimal amount)
        => decimal.Round(amount, Decimals, MidpointRounding.AwayFromZero);
}

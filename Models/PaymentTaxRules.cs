using System.Text.Json;
using System.Text.Json.Serialization;

namespace CameywareOrder.Models;

/// <summary>
/// How one payment method is taxed in a shop: either tax free, or taxable at its own rate.
/// The two are modelled separately rather than as "rate 0 means free" so a shop can park a rate
/// (say 13) on a method it has temporarily switched to tax free without losing the number.
/// </summary>
public sealed class PaymentTaxRule
{
    public bool IsTaxable { get; set; }

    public decimal RatePercent { get; set; }

    /// <summary>The rate actually charged: zero whenever the method is tax free.</summary>
    [JsonIgnore]
    public decimal EffectiveRatePercent => IsTaxable && RatePercent > 0m ? RatePercent : 0m;

    public PaymentTaxRule Clone() => new() { IsTaxable = IsTaxable, RatePercent = RatePercent };
}

/// <summary>
/// A shop's tax rules for every payment method it accepts — the single answer to "is this way of
/// paying taxed, and at what rate". Persisted on the shop row (<see cref="Shop.PaymentTaxRulesJson"/>)
/// so each branch charges what it is registered to charge.
///
/// The type lives in Models rather than Services because <see cref="Order.CalculateSectionPayment"/>
/// has to consult it: the money split cannot decide whether a portion is taxed without knowing the
/// shop's rules. <see cref="Active"/> is what the model reads; <c>PaymentTaxRuleService</c> owns
/// loading and saving it and is the only thing that assigns it.
/// </summary>
public sealed class PaymentTaxRules
{
    /// <summary>Standard Ontario HST rate, the default for both card types.</summary>
    public const decimal DefaultCardRatePercent = 13m;

    /// <summary>
    /// The methods a shop configures, in the order the settings screen lists them. "None" is the
    /// absence of a payment rather than a way of paying, so it is deliberately not configurable,
    /// and the legacy <see cref="PaymentMethod.Card"/> is not listed either — it resolves through
    /// <see cref="Normalize"/> to the debit rule.
    /// </summary>
    public static readonly PaymentMethod[] ConfigurableMethods =
    {
        PaymentMethod.Cash,
        PaymentMethod.DebitCard,
        PaymentMethod.CreditCard,
        PaymentMethod.Etransfer
    };

    /// <summary>
    /// The rules the app is currently working under. Assigned by <c>PaymentTaxRuleService</c> when
    /// a shop is opened or its settings are saved, so every consumer — the order editor, the money
    /// calculation, the receipt — reads one shared answer. Defaults keep a shop that has never
    /// opened the settings screen behaving exactly as the app always did.
    /// </summary>
    public static PaymentTaxRules Active { get; private set; } = CreateDefault();

    /// <summary>Rule per <see cref="PaymentMethod"/> name. Serialized by name so adding a method later cannot re-map existing entries.</summary>
    public Dictionary<string, PaymentTaxRule> Methods { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cash and e-transfer are tax free; both card types are taxable at the standard rate.</summary>
    public static PaymentTaxRules CreateDefault()
    {
        var rules = new PaymentTaxRules();

        foreach (var method in ConfigurableMethods)
        {
            var taxable = IsCardMethod(method);
            rules.Methods[method.ToString()] = new PaymentTaxRule
            {
                IsTaxable = taxable,
                RatePercent = taxable ? DefaultCardRatePercent : 0m
            };
        }

        return rules;
    }

    public static void SetActive(PaymentTaxRules rules)
        => Active = rules ?? CreateDefault();

    /// <summary>
    /// Maps the legacy single card value onto debit. Orders saved before debit and credit were
    /// separated recorded <see cref="PaymentMethod.Card"/>, whose label read "Card (Visa/Debit)" —
    /// so debit is what the shop was actually recording.
    /// </summary>
    public static PaymentMethod Normalize(PaymentMethod method)
        => method == PaymentMethod.Card ? PaymentMethod.DebitCard : method;

    public static bool IsCardMethod(PaymentMethod method)
        => method is PaymentMethod.Card or PaymentMethod.DebitCard or PaymentMethod.CreditCard;

    /// <summary>
    /// The rule for a method, created on demand from the defaults so a shop configured before a
    /// new payment method existed never reads back a missing entry.
    /// </summary>
    public PaymentTaxRule For(PaymentMethod method)
    {
        var key = Normalize(method).ToString();

        if (!Methods.TryGetValue(key, out var rule))
        {
            var taxable = IsCardMethod(method);
            rule = new PaymentTaxRule
            {
                IsTaxable = taxable,
                RatePercent = taxable ? DefaultCardRatePercent : 0m
            };
            Methods[key] = rule;
        }

        return rule;
    }

    /// <summary>True when paying this way attracts tax. A missing or "None" method never does.</summary>
    public bool IsTaxable(PaymentMethod? method)
        => method is not null and not PaymentMethod.None && For(method.Value).IsTaxable;

    /// <summary>The rate charged for this method, or zero when it is tax free / not a payment.</summary>
    public decimal RateFor(PaymentMethod? method)
        => method is null or PaymentMethod.None ? 0m : For(method.Value).EffectiveRatePercent;

    public PaymentTaxRules Clone()
    {
        var clone = new PaymentTaxRules();
        foreach (var (key, rule) in Methods)
            clone.Methods[key] = rule.Clone();
        return clone;
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Reads persisted rules, falling back to the defaults for null/blank/corrupt JSON.</summary>
    public static PaymentTaxRules FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return CreateDefault();

        try
        {
            return JsonSerializer.Deserialize<PaymentTaxRules>(json) ?? CreateDefault();
        }
        catch (JsonException)
        {
            return CreateDefault();
        }
    }
}

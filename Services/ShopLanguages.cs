using CameywareOrder.Localization;
using CameywareOrder.Models;

namespace CameywareOrder.Services;

/// <summary>
/// The one answer to "which languages may this session choose from". A shop installs one or more of
/// the shipped languages; its managers and staff switch between those and no others, while an
/// administrator — who works across every branch — keeps all of them.
/// </summary>
/// <remarks>
/// This lives outside both <c>AuthenticationService</c> and <c>ShopContext</c> because the answer is
/// a product of BOTH: a capability (may this person choose freely?) and a shop's configuration
/// (which languages does this branch run in?). Putting it in either would have left the other half
/// reaching across for the rest of it, and the rule is consumed by four screens — the main toolbar,
/// the shop editor, the measurement print dialog and the PDF download panel — which is exactly the
/// number of places a copied rule starts drifting in.
///
/// Every method takes what it needs rather than reading the singletons, so the rules are testable
/// against a shop that is not open and a language table that is not the installed one. The
/// parameterless <see cref="Selectable()"/> is the convenience the UI actually calls.
/// </remarks>
public static class ShopLanguages
{
    /// <summary>
    /// The languages <paramref name="shop"/> runs in. Never empty: a shop that has installed none
    /// falls back to its preferred language, which is precisely how the application behaved before
    /// a shop could install more than one — so an existing branch sees no change until somebody
    /// installs a second language for it.
    /// </summary>
    /// <remarks>
    /// Resolved against the languages that actually SHIP, in their order, so a code whose
    /// <c>*.lang.xml</c> has since been removed is dropped rather than offered as an option that
    /// renders every key as its own name. With no shop open nothing is restricted, so the answer is
    /// the whole shipped set — that is the state the login and shop-picker screens run in.
    /// </remarks>
    public static IReadOnlyList<LanguageOption> Installed(Shop? shop, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);

        var shipped = localization.AvailableLanguages;
        if (shop is null)
            return shipped;

        var wanted = shop.InstalledLanguageCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installed = shipped.Where(option => wanted.Contains(option.Code)).ToList();
        if (installed.Count > 0)
            return installed;

        // Nothing installed. The shop's preferred language is the closest thing it has said about
        // which language it runs in, and taking it literally reproduces the previous behaviour
        // exactly. A shop that has said nothing at all has restricted nothing at all.
        var preferred = shipped.FirstOrDefault(option =>
            string.Equals(option.Code, shop.PreferredLanguageCode, StringComparison.OrdinalIgnoreCase));

        return preferred is null ? shipped : new[] { preferred };
    }

    /// <summary>
    /// The languages the signed-in user may pick between in <paramref name="shop"/> — every shipped
    /// language for an administrator, the shop's installed set for everybody else.
    /// </summary>
    public static IReadOnlyList<LanguageOption> Selectable(
        Shop? shop, bool canChooseAnyLanguage, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);

        return canChooseAnyLanguage ? localization.AvailableLanguages : Installed(shop, localization);
    }

    /// <summary>
    /// <see cref="Selectable(Shop?, bool, LocalizationService)"/> for the session as it stands — the
    /// open shop, the signed-in user and the loaded language table.
    /// </summary>
    public static IReadOnlyList<LanguageOption> Selectable()
        => Selectable(
            ShopContext.Instance.Current,
            AuthenticationService.Instance.CanChooseAnyLanguage,
            LocalizationService.Instance);

    /// <summary>
    /// Whether a language toggle is worth showing at all. One language is not a choice, and a
    /// picker holding a single option is chrome that cannot do anything.
    /// </summary>
    public static bool CanToggle(Shop? shop, bool canChooseAnyLanguage, LocalizationService localization)
        => Selectable(shop, canChooseAnyLanguage, localization).Count > 1;

    /// <summary>
    /// Whether <paramref name="shop"/> runs in <paramref name="languageCode"/> — the test for
    /// whether the language already on screen may simply be kept when the shop opens.
    /// </summary>
    public static bool Offers(Shop? shop, string? languageCode, LocalizationService localization)
        => !string.IsNullOrWhiteSpace(languageCode)
           && Installed(shop, localization)
               .Any(option => string.Equals(option.Code, languageCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The language a shop opens in: its preferred one when it installs it, otherwise the first
    /// language it does install.
    /// </summary>
    /// <remarks>
    /// The fallback matters because the two fields can disagree — a shop configured before installed
    /// sets existed, or one whose preferred language was later uninstalled. Opening in a language the
    /// branch does not run in would leave its staff with a toggle that cannot return them to where
    /// they started.
    /// </remarks>
    public static string PreferredCode(Shop? shop, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);

        var installed = Installed(shop, localization);
        var preferred = installed.FirstOrDefault(option =>
            string.Equals(option.Code, shop?.PreferredLanguageCode, StringComparison.OrdinalIgnoreCase));

        return preferred?.Code ?? installed[0].Code;
    }

    /// <summary>
    /// Plain-language statement of what a shop runs in — "Installed languages: 简体中文, English" —
    /// shown under the greeting so anybody can see which languages their branch offers without
    /// opening the toggle to count them.
    /// </summary>
    /// <remarks>
    /// Always describes the SHOP, never the administrator's expanded choice: the useful fact is what
    /// this branch is configured for, and an administrator standing in it wants that answer too.
    ///
    /// Singular and plural are separate keys rather than one string with the count interpolated.
    /// English needs "language is" against "languages are", and which languages a shop installs is
    /// exactly the sort of line a reader skims — a grammatical stumble in it is noticed.
    /// </remarks>
    public static string InstalledSummary(Shop? shop, LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);

        var installed = Installed(shop, localization);
        var names = localization.JoinList(installed.Select(option => option.Name));

        return localization.Format(
            installed.Count == 1 ? "Language.Installed.One" : "Language.Installed.Many", names);
    }
}

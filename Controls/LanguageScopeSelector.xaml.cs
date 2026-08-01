using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CameywareOrder.Localization;
using CameywareOrder.Services;

namespace CameywareOrder.Controls;

/// <summary>
/// A picker that previews the panel it sits on in another language, leaving the application's own
/// language setting alone.
/// </summary>
/// <remarks>
/// <b>What it is for.</b> Checking a translation used to mean switching the whole application into a
/// language, finding the screen again, reading it, and switching back — for every language. This
/// makes that one drop-down on the screen being checked.
///
/// <b>How to put it on a panel.</b> Three lines, and it works on any panel:
/// <code>
/// &lt;Window.Resources&gt;
///     &lt;loc:LocalizationScope x:Key="Scope"/&gt;      &lt;!-- 1. the panel's own language --&gt;
/// &lt;/Window.Resources&gt;
/// ...
/// &lt;ctrl:LanguageScopeSelector Scope="{StaticResource Scope}"/&gt;   &lt;!-- 2. the picker --&gt;
/// ...
/// Text="{Binding Source={StaticResource Scope}, Path=[Some.Key], Mode=OneWay}"   &lt;!-- 3. bind --&gt;
/// </code>
/// Nothing else is wired: the control fills itself from the shop's installed languages, and the scope
/// re-renders every binding that reads it.
///
/// <b>It renders ITSELF in the application's language.</b> Its label, and each language's name in the
/// list, come from <see cref="LocalizationService.Instance"/> — never from the scope. A control that
/// followed its own preview would turn Japanese the moment Japanese was picked, leaving nothing on
/// screen the reader can use to get back. The picker has to stay readable in the language the person
/// actually speaks; the panel around it is the thing being previewed.
///
/// <b>Which languages.</b> <see cref="ShopLanguages.Selectable()"/>, the same rule as every other
/// language picker in the application — every shipped language for an administrator, the shop's
/// installed set for everybody else. A preview must not offer a language the branch does not run in:
/// what it would show is a translation nobody there will ever see.
/// </remarks>
[SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
    Justification = "Reads LanguageBox / PreviewLabel, x:Name fields declared in the XAML-generated " +
                    "partial that SonarLint's standalone single-file pass cannot see.")]
public partial class LanguageScopeSelector : UserControl
{
    /// <summary>
    /// The scope this picker drives. A dependency property so it can be pointed at a panel's
    /// <c>{StaticResource Scope}</c> in markup, with no code-behind on the host at all.
    /// </summary>
    public static readonly DependencyProperty ScopeProperty = DependencyProperty.Register(
        nameof(Scope),
        typeof(LocalizationScope),
        typeof(LanguageScopeSelector),
        new PropertyMetadata(null, OnScopeChanged));

    private readonly LocalizationService _localization = LocalizationService.Instance;
    private bool _populating;

    public LanguageScopeSelector()
    {
        InitializeComponent();

        // The quiet grey it used to hard-code, now only a DEFAULT: a host on a dark header sets its
        // own and the label follows. See the binding in the XAML.
        Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
        Populate();

        _localization.LanguageChanged += OnApplicationLanguageChanged;
        Unloaded += (_, _) =>
        {
            _localization.LanguageChanged -= OnApplicationLanguageChanged;
            if (Scope is not null)
                Scope.TextChanged -= OnScopeTextChanged;
        };
    }

    public LocalizationScope? Scope
    {
        get => (LocalizationScope?)GetValue(ScopeProperty);
        set => SetValue(ScopeProperty, value);
    }

    /// <summary>Raised after the picker has moved its scope, for a host that renders text in code.</summary>
    public event EventHandler? PreviewLanguageChanged;

    /// <summary>
    /// Follows the scope it is given, in BOTH directions.
    /// </summary>
    /// <remarks>
    /// Subscribing looks redundant — this control is usually the only thing that moves the scope —
    /// and it is not: a host may pin or <see cref="LocalizationScope.Follow"/> a scope itself, and a
    /// picker still naming the previous language is then describing something that is not on screen.
    /// Rendering the panel caught exactly that.
    /// </remarks>
    private static void OnScopeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not LanguageScopeSelector selector)
            return;

        if (e.OldValue is LocalizationScope previous)
            previous.TextChanged -= selector.OnScopeTextChanged;

        if (e.NewValue is LocalizationScope current)
            current.TextChanged += selector.OnScopeTextChanged;

        selector.SelectScopeLanguage();
    }

    private void OnScopeTextChanged(object? sender, EventArgs e) => SelectScopeLanguage();

    /// <summary>
    /// Fills the list and decides whether this control is worth showing at all.
    /// </summary>
    /// <remarks>
    /// Collapsed when the shop runs in one language, the same rule the main window's toggle and the
    /// measurement print dialog follow: a picker holding a single option is chrome that cannot do
    /// anything, and here it would also promise a comparison that cannot be made.
    /// </remarks>
    private void Populate()
    {
        var languages = ShopLanguages.Selectable();

        _populating = true;
        try
        {
            LanguageBox.ItemsSource = languages;
            PreviewLabel.Text = _localization["Language.PreviewLabel"];
            Visibility = languages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _populating = false;
        }

        SelectScopeLanguage();
    }

    /// <summary>Puts the box on whatever language the scope is actually resolving against.</summary>
    private void SelectScopeLanguage()
    {
        if (Scope is null)
            return;

        _populating = true;
        try
        {
            LanguageBox.SelectedValue = Scope.EffectiveLanguageCode;

            // A scope pinned to a language this shop does not install would otherwise leave the box
            // blank while the panel renders in it — the picker would be describing nothing.
            if (LanguageBox.SelectedIndex < 0 && LanguageBox.Items.Count > 0)
                LanguageBox.SelectedIndex = 0;
        }
        finally
        {
            _populating = false;
        }
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populating || Scope is null || LanguageBox.SelectedValue is not string code)
            return;

        Scope.LanguageCode = code;
        PreviewLanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The APPLICATION switched language. Re-label in the new one, re-scope the offered set (a shop
    /// switch can change it), and — only while the scope is still following — move the box with it.
    /// </summary>
    private void OnApplicationLanguageChanged(object? sender, EventArgs e)
    {
        var wasFollowing = Scope?.FollowsApplication ?? true;
        Populate();

        if (wasFollowing)
            Scope?.Follow();

        SelectScopeLanguage();
    }
}

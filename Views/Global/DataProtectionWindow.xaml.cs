using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CameywareOrder.Configuration;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;

namespace CameywareOrder.Views;

/// <summary>
/// How this installation protects itself: the backup schedule, the recycle-bin retention, and the
/// safety copies themselves.
/// </summary>
/// <remarks>
/// Two settings that a shop owner experiences as one question — "what happens if something goes
/// wrong" — so they share a panel rather than each getting one.
///
/// The window performs nothing itself. <c>BackupService</c> owns writing and restoring a copy,
/// <c>DataProtectionStore</c> owns the settings file, and <c>ConfirmDestructiveWindow</c> owns the
/// gate in front of a restore. What lives here is the wording, the ordering, and which of the three
/// buttons is dangerous enough to need the typed phrase.
/// </remarks>
public partial class DataProtectionWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly ObservableCollection<BackupRow> _rows = new();

    /// <summary>
    /// Suppresses the save-on-change handlers while the controls are being FILLED.
    /// </summary>
    /// <remarks>
    /// Assigning <c>SelectedItem</c> raises <c>SelectionChanged</c>, so seeding four controls from
    /// the stored settings would write the settings back four times before the user had touched
    /// anything — and the first of those writes would persist a half-seeded object. The same
    /// reentrancy guard the order editor uses for its radio groups.
    /// </remarks>
    private bool _loading;

    public DataProtectionWindow(LocalizationService localization)
    {
        InitializeComponent();

        _localization = localization;
        BackupList.ItemsSource = _rows;

        LoadSettings();
        LoadBackups();
    }

    /// <summary>
    /// True once a backup was restored. The caller must reload everything it is holding: the whole
    /// database has been replaced underneath it, including the shop it has open.
    /// </summary>
    public bool DataRestored { get; private set; }

    // ── settings ──────────────────────────────────────────────────────────────────────────────────

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var settings = DataProtectionStore.Instance.Settings;

            AutomaticBackupCheck.IsChecked = settings.AutomaticBackupEnabled;

            Fill(IntervalBox, DataProtectionSettings.IntervalChoices, settings.EffectiveIntervalHours,
                hours => _localization.Format("DataProtection.EveryHours", hours));

            Fill(RetentionBox, DataProtectionSettings.RetentionChoices, settings.BackupRetentionCount,
                count => _localization.Format("DataProtection.KeepCount", count));

            Fill(BinRetentionBox, DataProtectionSettings.RecycleBinChoices, settings.RecycleBinDays,
                days => _localization.Format("DataProtection.BinDays", days));
        }
        finally
        {
            _loading = false;
        }

        RefreshLastBackupText();
    }

    /// <summary>
    /// Fills a picker with the offered values, selecting the stored one — or ADDING it when the file
    /// holds something the list does not offer.
    /// </summary>
    /// <remarks>
    /// The stored value wins over the offered set. A number typed into the JSON by hand, or left over
    /// from a build whose choices were different, is still what this installation is running on;
    /// silently snapping the picker to the nearest offered value would show the user a setting that
    /// is not in force, and then save it the moment they changed anything else.
    /// </remarks>
    private static void Fill(
        System.Windows.Controls.ComboBox box, IReadOnlyList<int> choices, int selected, Func<int, string> label)
    {
        var values = choices.Contains(selected)
            ? choices.ToList()
            : choices.Concat(new[] { selected }).OrderBy(value => value).ToList();

        box.ItemsSource = values.Select(value => new ChoiceRow(value, label(value))).ToList();
        box.DisplayMemberPath = nameof(ChoiceRow.Label);
        box.SelectedIndex = values.IndexOf(selected);
    }

    /// <summary>
    /// Persists every setting on the panel whenever one of them changes.
    /// </summary>
    /// <remarks>
    /// No Save button, deliberately. Each of these is a single independent choice with an immediate
    /// meaning, and a panel with four pickers behind a Save is a panel people close having changed
    /// nothing. The whole object is written each time — <c>DataProtectionStore.Save</c> takes one —
    /// so a partial write cannot leave the file describing a state nobody chose.
    /// </remarks>
    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        var store = DataProtectionStore.Instance;
        var settings = store.Settings.Clone();

        settings.AutomaticBackupEnabled = AutomaticBackupCheck.IsChecked.GetValueOrDefault();
        settings.BackupIntervalHours = Chosen(IntervalBox, settings.BackupIntervalHours);
        settings.BackupRetentionCount = Chosen(RetentionBox, settings.BackupRetentionCount);
        settings.RecycleBinDays = Chosen(BinRetentionBox, settings.RecycleBinDays);

        store.Save(settings);
        RefreshLastBackupText();
    }

    private static int Chosen(System.Windows.Controls.ComboBox box, int fallback)
        => box.SelectedItem is ChoiceRow row ? row.Value : fallback;

    private void RefreshLastBackupText()
    {
        var settings = DataProtectionStore.Instance.Settings;

        // "Never" is a different statement from a date, and it is the one that should worry somebody
        // — an installation that has been running for a month and never backed up has a schedule that
        // is not working.
        LastBackupText.Text = settings.LastBackupUtc is { } last
            ? _localization.Format("DataProtection.LastBackup",
                last.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture))
            : _localization["DataProtection.NeverBackedUp"];
    }

    // ── the copies ────────────────────────────────────────────────────────────────────────────────

    private void LoadBackups()
    {
        _rows.Clear();

        foreach (var entry in BackupService.List())
            _rows.Add(BuildRow(entry));

        NoBackupsText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshBackupActionState();
    }

    private BackupRow BuildRow(BackupEntry entry)
    {
        var headline = entry.TakenAtLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

        var detail = _localization.JoinFragments(new[]
        {
            entry.FileName,
            _localization.Format("DataProtection.Size", Megabytes(entry.SizeBytes)),
        });

        var kind = entry.Kind == BackupKind.Package
            ? _localization["DataProtection.Kind.Package"]
            : _localization["DataProtection.Kind.PreImport"];

        return new BackupRow(entry, headline, detail, kind);
    }

    /// <summary>
    /// The size in megabytes, to one place. A backup of a shop with photographs is tens of MB and a
    /// byte count is unreadable at that scale; anything under 0.1 MB reads as 0.1 rather than 0.0,
    /// since a copy that reports zero size looks like a failed one.
    /// </summary>
    private static string Megabytes(long bytes)
        => Math.Max(0.1, Math.Round(bytes / 1024d / 1024d, 1)).ToString("0.0", CultureInfo.CurrentCulture);

    private void OnBackupSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => RefreshBackupActionState();

    private void RefreshBackupActionState()
    {
        var selected = BackupList.SelectedItem is BackupRow;
        RestoreButton.IsEnabled = selected;
        DeleteBackupButton.IsEnabled = selected;
    }

    private void OnBackupNowClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanManageBackups)
            return;

        var result = BackupService.RunNow(DateTime.UtcNow);

        LoadBackups();
        RefreshLastBackupText();

        if (result.Succeeded)
            ReportSuccess(_localization.Format("DataProtection.BackedUp", result.Detail));
        else
            ReportFailure(_localization.Format("DataProtection.BackupFailed", result.Detail));
    }

    /// <summary>
    /// Restores the selected copy over the live data.
    /// </summary>
    /// <remarks>
    /// Through <see cref="ConfirmDestructiveWindow"/> — the typed phrase — because this replaces
    /// EVERY shop's database and document images at once, which is the same class of act as deleting
    /// a shop and a good deal easier to reach by accident from a list of dates.
    ///
    /// The import path takes its own copy of what it is about to overwrite, so a restore of the wrong
    /// file is itself recoverable; the impact lines say so, because an irreversible-looking dialog in
    /// front of a reversible act teaches people to click through the ones that are not.
    /// </remarks>
    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedItem is not BackupRow row || !AuthenticationService.Instance.CanManageBackups)
            return;

        var impact = new List<string>
        {
            _localization.Format("DataProtection.RestoreImpactFrom", row.Headline),
            _localization["DataProtection.RestoreImpactReplaces"],
            _localization["DataProtection.RestoreImpactBacksUp"],
        };

        var confirm = new ConfirmDestructiveWindow(
            _localization,
            _localization["DataProtection.RestoreHeadline"],
            impact,
            _localization["DataProtection.RestoreNow"]) { Owner = this };

        if (confirm.ShowDialog() is not true)
            return;

        var result = BackupService.Restore(row.Entry);

        if (!result.Succeeded)
        {
            ReportFailure(_localization.Format("DataProtection.RestoreFailed", result.Detail));
            return;
        }

        DataRestored = true;
        LoadBackups();
        ReportSuccess(_localization["DataProtection.Restored"]);
    }

    private void OnDeleteBackupClick(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedItem is not BackupRow row || !AuthenticationService.Instance.CanManageBackups)
            return;

        // An ordinary Yes/No rather than the typed phrase: deleting ONE copy while others remain is
        // not the same act as overwriting the live data with it, and gating both identically would
        // make the phrase mean nothing.
        var answer = MessageBox.Show(
            _localization.Format("DataProtection.DeleteCopyConfirm", row.Headline),
            _localization["DataProtection.DeleteCopy"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        if (BackupService.Delete(row.Entry))
        {
            LoadBackups();
            ReportSuccess(_localization.Format("DataProtection.CopyDeleted", row.Headline));
        }
        else
        {
            ReportFailure(_localization.Format("DataProtection.CopyDeleteFailed", row.Headline));
        }
    }

    /// <summary>Opens the backups folder, so the shop can copy one onto a USB stick.</summary>
    /// <remarks>
    /// The single most valuable thing this panel offers a shop with no IT support: a backup that
    /// never leaves the machine does not survive the machine. Explorer by full path rather than by
    /// name, the same way the Local Database menu reveals the database file.
    /// </remarks>
    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(UserDataPaths.BackupsDirectory);

            Process.Start(new ProcessStartInfo
            {
                FileName = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                Arguments = $"\"{UserDataPaths.BackupsDirectory}\"",
                UseShellExecute = false,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ReportFailure(ex.Message);
        }
    }

    // ── plumbing ──────────────────────────────────────────────────────────────────────────────────

    private void ReportSuccess(string message) => Report(message, Color.FromRgb(0x04, 0x78, 0x57));

    private void ReportFailure(string message) => Report(message, Color.FromRgb(0xB9, 0x1C, 0x1C));

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "False positive: StatusText is an x:Name instance field from the XAML-generated " +
                        "partial, which SonarLint's single-file pass cannot see.")]
    private void Report(string message, Color colour)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(colour);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>One offered value in a settings picker, with the sentence that describes it.</summary>
    private sealed record ChoiceRow(int Value, string Label);

    /// <summary>One safety copy as the list renders it. Holds the entry so an action needs no lookup.</summary>
    private sealed record BackupRow(BackupEntry Entry, string Headline, string Detail, string KindText);
}

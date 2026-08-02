using System.Text.Json.Serialization;

namespace CameywareOrder.Models;

/// <summary>
/// How this installation looks after the shop's data: how often it backs itself up, how many copies
/// it keeps, and how long a deleted order stays recoverable.
/// </summary>
/// <remarks>
/// One model and one settings file for both, because to the person running the shop they are one
/// decision — "what happens if something goes wrong" — and splitting them would produce two panels
/// answering halves of the same question.
///
/// PER INSTALLATION, not per shop and not shipped. It describes the machine the application is
/// installed on: how much disk to spend on safety copies is a property of that machine, and a shop
/// carried to another PC in an export should not bring the old one's schedule with it. Stored in
/// <c>Config/data-protection.json</c> beside the language preference, which is the same kind of
/// thing.
///
/// Every value is nullable-free with a sane default, and the defaults reproduce the behaviour the
/// application had before this existed as closely as it can: <see cref="BackupRetentionCount"/>
/// starts from the shipped <c>app-defaults.json</c> figure, and the backup itself is ON by default
/// because the alternative — shipping the feature switched off — leaves exactly the shops that need
/// it most with no backups at all.
/// </remarks>
public sealed class DataProtectionSettings
{
    /// <summary>How often an automatic backup runs, when nothing else is configured.</summary>
    public const int DefaultIntervalHours = 24;

    /// <summary>How long a deleted order stays in the recycle bin, when nothing else is configured.</summary>
    public const int DefaultRecycleBinDays = 30;

    /// <summary>The intervals the settings panel offers, in hours.</summary>
    /// <remarks>
    /// A fixed list rather than a free number box. The honest range for a shop is "every time I open
    /// it", "once a day" and "once a week"; a box invites 0 and 9999, and both are ways of switching
    /// the feature off without saying so.
    /// </remarks>
    public static readonly IReadOnlyList<int> IntervalChoices = new[] { 6, 12, 24, 72, 168 };

    /// <summary>The retention windows the settings panel offers, in days.</summary>
    public static readonly IReadOnlyList<int> RecycleBinChoices = new[] { 7, 14, 30, 60, 90 };

    /// <summary>The copy counts the settings panel offers.</summary>
    public static readonly IReadOnlyList<int> RetentionChoices = new[] { 3, 5, 10, 20, 30 };

    /// <summary>Whether the application backs itself up on its own.</summary>
    public bool AutomaticBackupEnabled { get; set; } = true;

    /// <summary>Hours between automatic backups. Clamped to a sane range on read.</summary>
    public int BackupIntervalHours { get; set; } = DefaultIntervalHours;

    /// <summary>How many automatic backups to keep before the oldest is deleted.</summary>
    public int BackupRetentionCount { get; set; } = 10;

    /// <summary>When the last automatic backup ran. Null means never.</summary>
    public DateTime? LastBackupUtc { get; set; }

    /// <summary>
    /// How many days a deleted order stays in the recycle bin before it is removed for good.
    /// </summary>
    /// <remarks>
    /// Zero or less means "keep them until somebody empties the bin by hand". That is a legitimate
    /// answer for a shop that would rather decide itself than have the application decide, and it is
    /// deliberately reachable — a retention feature whose only settings all end in data being
    /// destroyed is one people switch off by not using it.
    /// </remarks>
    public int RecycleBinDays { get; set; } = DefaultRecycleBinDays;

    /// <summary>The interval to actually use — the stored value, clamped to what the panel offers.</summary>
    [JsonIgnore]
    public int EffectiveIntervalHours => Math.Clamp(BackupIntervalHours, 1, 24 * 30);

    /// <summary>Whether the bin purges on its own at all.</summary>
    [JsonIgnore]
    public bool PurgesAutomatically => RecycleBinDays > 0;

    /// <summary>
    /// Whether a backup is due at <paramref name="now"/>. A never-run installation is always due,
    /// which is what gets the very first copy written on the first launch after upgrading.
    /// </summary>
    public bool IsBackupDue(DateTime now)
    {
        if (!AutomaticBackupEnabled)
            return false;

        if (LastBackupUtc is not { } last)
            return true;

        // A stamp in the FUTURE means the clock moved backwards — a machine whose time was wrong and
        // has been corrected, which is not rare on a shop PC that has been off for a month. Treating
        // it as "not due" would suspend backups until the future caught up with itself.
        return last > now || now - last >= TimeSpan.FromHours(EffectiveIntervalHours);
    }

    /// <summary>The instant before which a deleted order should be purged, or null when nothing is.</summary>
    public DateTime? PurgeBefore(DateTime now)
        => PurgesAutomatically ? now.AddDays(-RecycleBinDays) : null;

    public DataProtectionSettings Clone() => new()
    {
        AutomaticBackupEnabled = AutomaticBackupEnabled,
        BackupIntervalHours = BackupIntervalHours,
        BackupRetentionCount = BackupRetentionCount,
        LastBackupUtc = LastBackupUtc,
        RecycleBinDays = RecycleBinDays,
    };
}

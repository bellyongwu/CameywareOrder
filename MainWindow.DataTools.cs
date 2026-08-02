using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics.CodeAnalysis;
using CameywareOrder.Controls;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using CameywareOrder.ViewModels;
using CameywareOrder.Views;

namespace CameywareOrder;

public partial class MainWindow
{
    // Local Configuration's whole-installation entries: the database path tools, every import and export pair, and the panels reached from them. Each re-checks its capability — a hidden menu is a fact about the UI, not a permission.

    // Defence in depth on every handler below the Local Database and Import/Export menus: those menus are
    // hidden for non-administrators, but a hidden menu is a fact about the UI, not a permission.
    private void OnOpenDataFolderClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        try
        {
            var dbPath = DatabasePathProvider.DatabaseFilePath;
            var folderPath = System.IO.Path.GetDirectoryName(dbPath);
            if (string.IsNullOrWhiteSpace(folderPath))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = ExplorerPath,
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.OpenFolderFailed", ex.Message);
        }
    }

    private void OnCopyDataPathClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        try
        {
            Clipboard.SetText(DatabasePathProvider.DatabaseFilePath);
            _viewModel.StatusMessage = _localization["Status.CopyPathSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.CopyPathFailed", ex.Message);
        }
    }

    private void OnRevealDataFileClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        try
        {
            var dbPath = DatabasePathProvider.DatabaseFilePath;
            if (!System.IO.File.Exists(dbPath))
            {
                OnOpenDataFolderClick(sender, e);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = ExplorerPath,
                Arguments = $"/select,\"{dbPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.RevealFileFailed", ex.Message);
        }
    }

    // --- Import / export (Local Configuration → Import/Export) ----------------------------------

    // Appends today's date (yyyyMMdd) before the extension so exported files sort/archive
    // cleanly by date, e.g. "measurement-terms-20260726.json".
    private static string BuildDatedExportFileName(string baseName, string extension) =>
        $"{baseName}-{DateTime.Now:yyyyMMdd}.{extension}";

    private void OnExportMeasurementTermsClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        var dialog = new SaveFileDialog
        {
            FileName = BuildDatedExportFileName("measurement-terms", "json"),
            Filter = JsonFileFilter
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        try
        {
            System.IO.File.WriteAllText(dialog.FileName, MeasurementTermsService.Instance.ExportConfigJson());
            _viewModel.StatusMessage = _localization["Status.ExportMeasurementTermsSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ExportMeasurementTermsFailed", ex.Message);
        }
    }

    private void OnImportMeasurementTermsClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        var dialog = new OpenFileDialog
        {
            Filter = JsonFileFilter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        MeasurementTermsConfig? imported;
        try
        {
            imported = MeasurementTermsService.TryParseConfigJson(System.IO.File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ImportMeasurementTermsFailed", ex.Message);
            return;
        }

        if (imported is null)
        {
            MessageBox.Show(
                _localization["Status.ImportMeasurementTermsInvalid"],
                _localization["MeasureTerms.Title"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            _localization["ImportExport.MeasurementTermsConfirm"],
            _localization["MeasureTerms.Title"],
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            MeasurementTermsService.Instance.ImportConfig(imported);
            _viewModel.StatusMessage = _localization["Status.ImportMeasurementTermsSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ImportMeasurementTermsFailed", ex.Message);
        }
    }

    private void OnExportDatabaseClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        // The exported package is a zip containing orders.db plus every attached
        // custom-made document image, so the export is self-contained and can be
        // copied to another PC without leaving image references dangling.
        var dialog = new SaveFileDialog
        {
            FileName = BuildDatedExportFileName("orders-backup", "zip"),
            Filter = "Backup Package (*.zip)|*.zip"
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        try
        {
            DatabasePathProvider.ExportDatabaseTo(dialog.FileName);
            _viewModel.StatusMessage = _localization["Status.ExportDatabaseSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ExportDatabaseFailed", ex.Message);
        }
    }

    private void OnImportDatabaseClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        // Accepts the zip package produced by export (db + document images) as well as a
        // legacy raw .db file exported before document packaging existed.
        var dialog = new OpenFileDialog
        {
            Filter = "Backup Package (*.zip)|*.zip|SQLite Database (*.db)|*.db|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        // Destructive: replaces every order currently in the app. Requires explicit
        // confirmation; the current database is still auto-backed-up as an extra safety
        // net (see DatabasePathProvider.ImportDatabaseFrom).
        var confirm = MessageBox.Show(
            _localization["ImportExport.DatabaseConfirm"],
            _localization["Toolbar.LocalDatabase"],
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            DatabasePathProvider.ImportDatabaseFrom(dialog.FileName);
            _viewModel.LoadOrdersCommand.Execute(null);
            _viewModel.StatusMessage = _localization["Status.ImportDatabaseSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ImportDatabaseFailed", ex.Message);
        }
    }

    private void OnExportBrandingClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        var dialog = new SaveFileDialog
        {
            FileName = BuildDatedExportFileName("header-footer-branding", "json"),
            Filter = JsonFileFilter
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        try
        {
            System.IO.File.WriteAllText(dialog.FileName, ReceiptBrandingStore.ExportConfigJson());
            _viewModel.StatusMessage = _localization["Status.ExportBrandingSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ExportBrandingFailed", ex.Message);
        }
    }

    private void OnImportBrandingClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        var dialog = new OpenFileDialog
        {
            Filter = JsonFileFilter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        BrandingExport? imported;
        try
        {
            imported = ReceiptBrandingStore.TryParseConfigJson(System.IO.File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ImportBrandingFailed", ex.Message);
            return;
        }

        if (imported is null)
        {
            MessageBox.Show(
                _localization["Status.ImportBrandingInvalid"],
                _localization["Toolbar.HeaderFooter"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            _localization["ImportExport.BrandingConfirm"],
            _localization["Toolbar.HeaderFooter"],
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            ReceiptBrandingStore.ImportConfig(imported);
            _viewModel.StatusMessage = _localization["Status.ImportBrandingSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ImportBrandingFailed", ex.Message);
        }
    }

    // One-click backup of everything this machine holds: the order database with its attached
    // images, measurement terms, receipt branding (logo included), currency and language.
    private void OnExportGlobalSettingsClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        var dialog = new SaveFileDialog
        {
            FileName = BuildDatedExportFileName("leeyonge-global-settings", "zip"),
            Filter = "Backup Package (*.zip)|*.zip"
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        try
        {
            GlobalSettingsPackage.ExportTo(dialog.FileName);
            _viewModel.StatusMessage = _localization["Status.ExportGlobalSettingsSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ExportGlobalSettingsFailed", ex.Message);
        }
    }

    private void OnImportGlobalSettingsClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanUseDataTools)
            return;

        var dialog = new OpenFileDialog
        {
            Filter = "Backup Package (*.zip)|*.zip|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) is not true)
            return;

        // Read and validate before touching anything, so an unreadable file changes nothing.
        var payload = GlobalSettingsPackage.TryRead(dialog.FileName);
        if (payload is null)
        {
            MessageBox.Show(
                _localization["Status.ImportGlobalSettingsInvalid"],
                _localization["Toolbar.GlobalSettings"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // This is the most destructive import in the app — it replaces the order data as well
        // as every local setting — so the confirmation spells out what the package will apply.
        var confirm = MessageBox.Show(
            _localization.Format("ImportExport.GlobalSettingsConfirm", DescribePackageContents(payload)),
            _localization["Toolbar.GlobalSettings"],
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            GlobalSettingsPackage.Import(dialog.FileName, payload);
            _viewModel.LoadOrdersCommand.Execute(null);
            _viewModel.StatusMessage = _localization["Status.ImportGlobalSettingsSucceeded"];
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = _localization.Format("Status.ImportGlobalSettingsFailed", ex.Message);
        }
    }

    // Lists only the parts the package actually carries, so the confirmation never promises to
    // restore something the file does not contain.
    private string DescribePackageContents(GlobalSettingsExport payload)
    {
        var parts = new List<string>();
        if (payload.ContainsDatabase)
            parts.Add(_localization["Toolbar.LocalDatabase"]);
        if (payload.MeasurementTerms is not null)
            parts.Add(_localization["Toolbar.MeasurementTerms"]);
        if (payload.Branding is not null)
            parts.Add(_localization["Toolbar.HeaderFooter"]);
        if (payload.Currency is not null)
            parts.Add(_localization["Toolbar.CurrencySetting"]);
        if (!string.IsNullOrWhiteSpace(payload.LanguageCode))
            parts.Add(_localization["Toolbar.Language"]);

        return _localization.JoinList(parts);
    }

    /// <summary>
    /// Saves the filtered order list as a spreadsheet.
    /// </summary>
    /// <remarks>
    /// The dialog lives here and the content lives in the view model — the split this codebase makes
    /// everywhere a file is written, because a view model that opens a dialog cannot be driven by a
    /// harness.
    ///
    /// An EMPTY result is refused before the dialog rather than after it. A shop that has filtered
    /// down to nothing and pressed Export should be told so, not handed a file picker and then a
    /// header row.
    /// </remarks>
    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanExportOrders)
            return;

        var (csv, fileName, rowCount) = _viewModel.BuildOrderExport();

        if (rowCount == 0)
        {
            _viewModel.StatusMessage = _localization["Csv.Export.Nothing"];
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = _localization["Csv.Export.Filter"],
            Title = _localization["Csv.Export.Action"],
            FileName = fileName,
        };

        // GetValueOrDefault rather than `is true`: the bool? is CONSUMED as a bool here, and Sonar
        // flags `is true` in that position (S1125). Behaviourally identical for bool?.
        if (!dialog.ShowDialog(this).GetValueOrDefault())
            return;

        try
        {
            csv.Save(dialog.FileName);
            _viewModel.ReportExport(succeeded: true,
                _localization.Format("Csv.Export.Rows", rowCount, dialog.FileName));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _viewModel.ReportExport(succeeded: false, ex.Message);
        }
    }

    /// <summary>
    /// Opens the recycle bin, and reloads the list if anything came back from it or went for good.
    /// </summary>
    private void OnRecycleBinClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanManageRecycleBin)
            return;

        var bin = new RecycleBinWindow(_localization, _scopeFactory) { Owner = this };
        bin.ShowDialog();

        if (bin.OrdersChanged)
            _viewModel.LoadOrdersCommand.Execute(null);
    }

    /// <summary>
    /// Opens the data-protection panel.
    /// </summary>
    /// <remarks>
    /// A restore performed there replaces the whole database — including the shop this window has
    /// open — so the list is reloaded afterwards. It is NOT enough on its own: the shop row itself
    /// may now be a different one, which is why the panel says to reopen the shop. Reloading is what
    /// stops the screen showing rows that no longer exist in the meantime.
    /// </remarks>
    private void OnDataProtectionClick(object sender, RoutedEventArgs e)
    {
        if (!AuthenticationService.Instance.CanManageBackups)
            return;

        var panel = new DataProtectionWindow(_localization) { Owner = this };
        panel.ShowDialog();

        if (panel.DataRestored)
            _viewModel.LoadOrdersCommand.Execute(null);
    }
}

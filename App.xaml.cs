using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.Windows;
using LeeYongeOrdering.Data;
using LeeYongeOrdering.GraphQL;
using LeeYongeOrdering.Localization;
using LeeYongeOrdering.ViewModels;
using LeeYongeOrdering.Views;

namespace LeeYongeOrdering;

public partial class App : Application
{
    private const string ServerScheme = "http";
    private const string ServerHost = "localhost";
    private const int ServerPort = 5050;
    private IHost? _host;
    private readonly LanguagePreferenceStore _languagePreferenceStore = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Prevent WPF from shutting down when the language picker closes.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var localization = LocalizationService.Instance;
        var languageFilePath = ResolveLanguageFilePath();
        localization.LoadFromFile(languageFilePath);

        DatabasePathProvider.EnsureDatabasePathReady();

        var savedLanguageCode = LanguagePreferenceStore.TryLoadLanguageCode();
        if (!string.IsNullOrWhiteSpace(savedLanguageCode))
            localization.SetLanguage(savedLanguageCode);

        localization.LanguageChanged += (_, _) =>
            _languagePreferenceStore.SaveLanguageCode(localization.CurrentLanguageCode);

        var languageDialog = new LanguageSelectionWindow(localization);
        if (languageDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(languageDialog.SelectedLanguageCode))
        {
            localization.SetLanguage(languageDialog.SelectedLanguageCode);
            _languagePreferenceStore.SaveLanguageCode(localization.CurrentLanguageCode);
        }

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug(); // visible in VS Output window only
            })
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls($"{ServerScheme}://{ServerHost}:{ServerPort}");
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGraphQL());
                });
            })
            .ConfigureServices(services =>
            {
                // SQLite – data stored at <AppDir>/orders.db
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite(DatabasePathProvider.ConnectionString));

                services.AddSingleton(localization);

                // Hot Chocolate GraphQL schema
                services
                    .AddGraphQLServer()
                    .AddQueryType<Query>()
                    .AddMutationType<Mutation>()
                    .AddFiltering()
                    .AddSorting()
                    .RegisterDbContext<AppDbContext>();

                // WPF services
                services.AddTransient<MainViewModel>();
                services.AddTransient<MainWindow>();
            })
            .Build();

        // Create tables if the database doesn't exist yet
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await EnsureDatabaseCompatibilityAsync(db);
            await EnsureMigrationBaselineAsync(db);
            await db.Database.MigrateAsync();
        }

        // Start Kestrel + all hosted services
        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    }

    private static string ResolveLanguageFilePath()
    {
        var inAppDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Languages.xml");
        if (System.IO.File.Exists(inAppDirectory))
            return inAppDirectory;

        var inWorkingDirectory = System.IO.Path.Combine(Environment.CurrentDirectory, "Languages.xml");
        if (System.IO.File.Exists(inWorkingDirectory))
            return inWorkingDirectory;

        throw new System.IO.FileNotFoundException("Languages.xml was not found.");
    }

    private static readonly (string Column, string Ddl)[] OrderColumnMigrations =
    {
        ("PhoneNumber", "ALTER TABLE Orders ADD COLUMN PhoneNumber TEXT NOT NULL DEFAULT ''; "),
        ("Email", "ALTER TABLE Orders ADD COLUMN Email TEXT NULL; "),
        ("Address", "ALTER TABLE Orders ADD COLUMN Address TEXT NULL; "),
        ("CurrencyType", "ALTER TABLE Orders ADD COLUMN CurrencyType INTEGER NOT NULL DEFAULT 1; "),
        ("ServiceType", "ALTER TABLE Orders ADD COLUMN ServiceType INTEGER NOT NULL DEFAULT 1; "),
        ("ServiceDetails", "ALTER TABLE Orders ADD COLUMN ServiceDetails TEXT NULL; "),
        ("AdditionalNotes", "ALTER TABLE Orders ADD COLUMN AdditionalNotes TEXT NULL; "),
        ("Subtotal", "ALTER TABLE Orders ADD COLUMN Subtotal TEXT NULL; "),
        ("TaxRate", "ALTER TABLE Orders ADD COLUMN TaxRate TEXT NULL; "),
        ("AlterationSubtotal", "ALTER TABLE Orders ADD COLUMN AlterationSubtotal TEXT NULL; "),
        ("AlterationTaxRate", "ALTER TABLE Orders ADD COLUMN AlterationTaxRate TEXT NULL; "),
        ("ClothingSubtotal", "ALTER TABLE Orders ADD COLUMN ClothingSubtotal TEXT NULL; "),
        ("ClothingTaxRate", "ALTER TABLE Orders ADD COLUMN ClothingTaxRate TEXT NULL; "),
        ("CustomMadeTaxRate", "ALTER TABLE Orders ADD COLUMN CustomMadeTaxRate TEXT NULL; "),
        ("ChestSize", "ALTER TABLE Orders ADD COLUMN ChestSize TEXT NULL; "),
        ("JacketLength", "ALTER TABLE Orders ADD COLUMN JacketLength TEXT NULL; "),
        ("CustomMadeRecordsJson", "ALTER TABLE Orders ADD COLUMN CustomMadeRecordsJson TEXT NULL; "),
        ("AlterationDownpayment", "ALTER TABLE Orders ADD COLUMN AlterationDownpayment TEXT NULL; "),
        ("AlterationDownpaymentMethod", "ALTER TABLE Orders ADD COLUMN AlterationDownpaymentMethod INTEGER NULL; "),
        ("AlterationDownpaymentCompleted", "ALTER TABLE Orders ADD COLUMN AlterationDownpaymentCompleted INTEGER NOT NULL DEFAULT 0; "),
        ("AlterationFinalBalanceMethod", "ALTER TABLE Orders ADD COLUMN AlterationFinalBalanceMethod INTEGER NULL; "),
        ("AlterationBalanceCleared", "ALTER TABLE Orders ADD COLUMN AlterationBalanceCleared INTEGER NOT NULL DEFAULT 0; "),
        ("CustomMadeDownpayment", "ALTER TABLE Orders ADD COLUMN CustomMadeDownpayment TEXT NULL; "),
        ("CustomMadeDownpaymentMethod", "ALTER TABLE Orders ADD COLUMN CustomMadeDownpaymentMethod INTEGER NULL; "),
        ("CustomMadeDownpaymentCompleted", "ALTER TABLE Orders ADD COLUMN CustomMadeDownpaymentCompleted INTEGER NOT NULL DEFAULT 0; "),
        ("CustomMadeFinalBalanceMethod", "ALTER TABLE Orders ADD COLUMN CustomMadeFinalBalanceMethod INTEGER NULL; "),
        ("CustomMadeBalanceCleared", "ALTER TABLE Orders ADD COLUMN CustomMadeBalanceCleared INTEGER NOT NULL DEFAULT 0; "),
        ("ClothingDownpayment", "ALTER TABLE Orders ADD COLUMN ClothingDownpayment TEXT NULL; "),
        ("ClothingDownpaymentMethod", "ALTER TABLE Orders ADD COLUMN ClothingDownpaymentMethod INTEGER NULL; "),
        ("ClothingDownpaymentCompleted", "ALTER TABLE Orders ADD COLUMN ClothingDownpaymentCompleted INTEGER NOT NULL DEFAULT 0; "),
        ("ClothingFinalBalanceMethod", "ALTER TABLE Orders ADD COLUMN ClothingFinalBalanceMethod INTEGER NULL; "),
        ("ClothingBalanceCleared", "ALTER TABLE Orders ADD COLUMN ClothingBalanceCleared INTEGER NOT NULL DEFAULT 0; "),
    };

    private static async Task EnsureDatabaseCompatibilityAsync(AppDbContext db)
    {
        var schema = await ReadOrdersSchemaAsync(db);
        if (!schema.HasOrdersTable)
            return;

        foreach (var (column, ddl) in OrderColumnMigrations)
        {
            if (!schema.OrderColumns.Contains(column))
                await db.Database.ExecuteSqlRawAsync(ddl);
        }

        if (schema.HasOrderItemsTable && !schema.OrderItemColumns.Contains("PromotionalPrice"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE OrderItems ADD COLUMN PromotionalPrice TEXT NULL; ");

        // Legacy status normalization:
        // old Pending(0) is now mapped to Processing(1).
        await db.Database.ExecuteSqlRawAsync("UPDATE Orders SET Status = 1 WHERE Status = 0;");
    }

    private static async Task<(bool HasOrdersTable, bool HasOrderItemsTable, HashSet<string> OrderColumns, HashSet<string> OrderItemColumns)> ReadOrdersSchemaAsync(AppDbContext db)
    {
        var orderColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderItemColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasOrdersTable;
        bool hasOrderItemsTable;

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            hasOrdersTable = await TableExistsAsync(connection, "Orders");
            hasOrderItemsTable = await TableExistsAsync(connection, "OrderItems");

            if (hasOrdersTable)
                await ReadColumnNamesAsync(connection, "PRAGMA table_info('Orders');", orderColumns);

            if (hasOrderItemsTable)
                await ReadColumnNamesAsync(connection, "PRAGMA table_info('OrderItems');", orderItemColumns);
        }
        finally
        {
            await connection.CloseAsync();
        }

        return (hasOrdersTable, hasOrderItemsTable, orderColumns, orderItemColumns);
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static async Task ReadColumnNamesAsync(DbConnection connection, string pragmaSql, HashSet<string> columns)
    {
        // PRAGMA table_info: cid, name, type, notnull, dflt_value, pk
        using var command = connection.CreateCommand();
        command.CommandText = pragmaSql;
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));
    }

    private static async Task EnsureMigrationBaselineAsync(AppDbContext db)
    {
        bool hasOrdersTable = false;
        bool hasHistoryTable = false;

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            using (var ordersCheckCommand = connection.CreateCommand())
            {
                ordersCheckCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Orders';";
                hasOrdersTable = Convert.ToInt32(await ordersCheckCommand.ExecuteScalarAsync()) > 0;
            }

            if (!hasOrdersTable)
                return;

            using (var historyCheckCommand = connection.CreateCommand())
            {
                historyCheckCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory';";
                hasHistoryTable = Convert.ToInt32(await historyCheckCommand.ExecuteScalarAsync()) > 0;
            }

            if (!hasHistoryTable)
            {
                await db.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (
                        MigrationId TEXT NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY,
                        ProductVersion TEXT NOT NULL
                    );");
            }

            // Mark the existing schema as the initial migration baseline.
            await db.Database.ExecuteSqlRawAsync(@"
                INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion)
                VALUES ('20260723015334_InitialCreate', '7.0.20');");
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }
        base.OnExit(e);
    }
}

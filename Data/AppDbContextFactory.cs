using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Diagnostics.CodeAnalysis;

namespace CameywareOrder.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Must remain an instance method to implement IDesignTimeDbContextFactory<AppDbContext>.")]
    public AppDbContext CreateDbContext(string[] args)
    {
        DatabasePathProvider.EnsureDatabasePathReady();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite(DatabasePathProvider.ConnectionString);

        var db = new AppDbContext(optionsBuilder.Options);
        EnsureLegacyBaseline(db);
        return db;
    }

    private static void EnsureLegacyBaseline(AppDbContext db)
    {
        bool hasOrdersTable;

        var connection = db.Database.GetDbConnection();
        connection.Open();
        try
        {
            using (var ordersCheck = connection.CreateCommand())
            {
                ordersCheck.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Orders';";
                hasOrdersTable = Convert.ToInt32(ordersCheck.ExecuteScalar()) > 0;
            }

            if (!hasOrdersTable)
                return;

            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (
                    MigrationId TEXT NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY,
                    ProductVersion TEXT NOT NULL
                );");

            db.Database.ExecuteSqlRaw(@"
                INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion)
                VALUES ('20260723015334_InitialCreate', '7.0.20');");
        }
        finally
        {
            connection.Close();
        }
    }
}

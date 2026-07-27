using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CameywareOrder.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPaymentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Downpayment",
                table: "Orders",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DownpaymentMethod",
                table: "Orders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FinalBalanceMethod",
                table: "Orders",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Downpayment",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DownpaymentMethod",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FinalBalanceMethod",
                table: "Orders");
        }
    }
}

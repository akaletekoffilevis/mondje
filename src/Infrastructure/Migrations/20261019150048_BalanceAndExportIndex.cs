using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mondje.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BalanceAndExportIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Balance",
                table: "Transactions",
                column: "CreatedAtUtc",
                filter: "[DeletedAtUtc] IS NULL")
                .Annotation("SqlServer:Include", new[] { "Type", "Amount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_Balance",
                table: "Transactions");
        }
    }
}

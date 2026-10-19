using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mondje.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropAgencyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_Agency",
                table: "Transactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Agency",
                table: "Transactions",
                column: "Agency");
        }
    }
}

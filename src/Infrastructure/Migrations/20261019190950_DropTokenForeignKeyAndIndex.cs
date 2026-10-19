using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mondje.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropTokenForeignKeyAndIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransactionTokens_Transactions_TransactionId",
                table: "TransactionTokens");

            migrationBuilder.DropIndex(
                name: "IX_TransactionTokens_TransactionId",
                table: "TransactionTokens");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TransactionTokens_TransactionId",
                table: "TransactionTokens",
                column: "TransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_TransactionTokens_Transactions_TransactionId",
                table: "TransactionTokens",
                column: "TransactionId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

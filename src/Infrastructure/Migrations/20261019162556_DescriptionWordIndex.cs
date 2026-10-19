using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mondje.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DescriptionWordIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransactionTokens",
                columns: table => new
                {
                    Token = table.Column<string>(type: "nvarchar(64)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    TransactionId = table.Column<string>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionTokens", x => new { x.Token, x.CreatedAtUtc, x.TransactionId });
                    table.ForeignKey(
                        name: "FK_TransactionTokens_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionTokens_TransactionId",
                table: "TransactionTokens",
                column: "TransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransactionTokens");
        }
    }
}

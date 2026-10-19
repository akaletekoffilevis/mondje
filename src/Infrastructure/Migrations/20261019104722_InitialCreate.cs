using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mondje.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "char(36)", nullable: false),
                    Type = table.Column<string>(type: "varchar(6)", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Agency = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceLine = table.Column<int>(type: "int", nullable: true),
                    EditedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.CheckConstraint("CK_Transactions_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_Transactions_Description", "LEN([Description]) <= 255");
                    table.CheckConstraint("CK_Transactions_IdLower", "[Id] = LOWER([Id])");
                    table.CheckConstraint("CK_Transactions_Type", "[Type] IN ('CREDIT', 'DEBIT')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Agency",
                table: "Transactions",
                column: "Agency");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CreatedAtUtc_Id",
                table: "Transactions",
                columns: new[] { "CreatedAtUtc", "Id" },
                filter: "[DeletedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ImportId",
                table: "Transactions",
                column: "ImportId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Type_Amount",
                table: "Transactions",
                columns: new[] { "Type", "Amount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}

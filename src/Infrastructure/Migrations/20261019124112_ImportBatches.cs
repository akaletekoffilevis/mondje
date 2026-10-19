using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mondje.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ImportBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Format = table.Column<string>(type: "varchar(10)", nullable: false),
                    Agency = table.Column<string>(type: "nvarchar(32)", nullable: true),
                    Status = table.Column<string>(type: "varchar(10)", nullable: false),
                    TotalLines = table.Column<int>(type: "int", nullable: false),
                    InsertedCount = table.Column<int>(type: "int", nullable: false),
                    DuplicateCount = table.Column<int>(type: "int", nullable: false),
                    ErrorCount = table.Column<int>(type: "int", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(512)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RequestId = table.Column<string>(type: "nvarchar(64)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                    table.CheckConstraint("CK_ImportBatches_Format", "[Format] IN ('TXT', 'NDJSON')");
                    table.CheckConstraint("CK_ImportBatches_Status", "[Status] IN ('PENDING', 'RUNNING', 'COMPLETED', 'FAILED', 'CANCELLED')");
                });

            migrationBuilder.CreateTable(
                name: "ImportErrorLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(512)", nullable: false),
                    RawLine = table.Column<string>(type: "nvarchar(1000)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportErrorLines", x => x.Id);
                    table.CheckConstraint("CK_ImportErrorLines_Kind", "[Kind] IN ('FORMAT', 'VALIDATION')");
                    table.ForeignKey(
                        name: "FK_ImportErrorLines_ImportBatches_ImportId",
                        column: x => x.ImportId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_CreatedAtUtc_Id",
                table: "ImportBatches",
                columns: new[] { "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_Status",
                table: "ImportBatches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ImportErrorLines_ImportId_LineNumber",
                table: "ImportErrorLines",
                columns: new[] { "ImportId", "LineNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportErrorLines");


            migrationBuilder.DropTable(
                name: "ImportBatches");
        }
    }
}

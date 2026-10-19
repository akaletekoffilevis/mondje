using Mondje.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mondje.Infrastructure.Data.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnType("char(36)")
            .ValueGeneratedNever();

        builder.Property(t => t.Type)
            .HasColumnType("varchar(6)");

        builder.Property(t => t.Amount)
            .HasColumnType("bigint");

        builder.Property(t => t.Description)
            .HasMaxLength(Transaction.MaxDescriptionLength)
            .HasColumnType("nvarchar(255)");

        builder.Property(t => t.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(t => t.Agency)
            .HasMaxLength(32)
            .HasColumnType("nvarchar(32)");

        builder.Property(t => t.ImportId)
            .HasColumnType("uniqueidentifier");

        builder.Property(t => t.SourceLine)
            .HasColumnType("int");

        builder.Property(t => t.EditedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(t => t.DeletedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_Transactions_Type", "[Type] IN ('CREDIT', 'DEBIT')");
            table.HasCheckConstraint("CK_Transactions_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_Transactions_Description", "LEN([Description]) <= 255");
            table.HasCheckConstraint("CK_Transactions_IdLower", "[Id] = LOWER([Id])");
        });

        // Keyset (cursor) pagination over live rows: ORDER BY CreatedAtUtc, Id.
        builder.HasIndex(t => new { t.CreatedAtUtc, t.Id })
            .HasDatabaseName("IX_Transactions_CreatedAtUtc_Id")
            .HasFilter("[DeletedAtUtc] IS NULL");

        // Search by type + amount range (challenge 3).
        builder.HasIndex(t => new { t.Type, t.Amount })
            .HasDatabaseName("IX_Transactions_Type_Amount");

        // Import provenance lookup (challenge 7) and surgical cancellation (challenge 9).
        builder.HasIndex(t => t.ImportId)
            .HasDatabaseName("IX_Transactions_ImportId");

        // Balance per agency (bonus challenge 5).
        builder.HasIndex(t => t.Agency)
            .HasDatabaseName("IX_Transactions_Agency");

        // Balance at a past date without recalculating the table (challenge 11): narrow
        // covering index over live rows, so SUM(CASE WHEN Type ...) is index-only.
        builder.HasIndex(t => t.CreatedAtUtc)
            .HasDatabaseName("IX_Transactions_Balance")
            .IncludeProperties(t => new { t.Type, t.Amount })
            .HasFilter("[DeletedAtUtc] IS NULL");
    }
}

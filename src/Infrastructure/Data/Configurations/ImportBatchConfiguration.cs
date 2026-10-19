using Mondje.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mondje.Infrastructure.Data.Configurations;

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("ImportBatches");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever();

        builder.Property(b => b.FileName)
            .HasColumnType("nvarchar(255)");

        builder.Property(b => b.StoredFileName)
            .IsRequired()
            .HasColumnType("nvarchar(255)");

        builder.Property(b => b.FileSizeBytes)
            .HasColumnType("bigint");

        builder.Property(b => b.Format)
            .IsRequired()
            .HasColumnType("varchar(10)");

        builder.Property(b => b.Agency)
            .HasColumnType("nvarchar(32)");

        builder.Property(b => b.Status)
            .IsRequired()
            .HasColumnType("varchar(10)");

        builder.Property(b => b.FailureReason)
            .HasColumnType("nvarchar(512)");

        builder.Property(b => b.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(b => b.StartedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(b => b.CompletedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(b => b.RequestId)
            .HasColumnType("nvarchar(64)");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_ImportBatches_Format", "[Format] IN ('TXT', 'NDJSON')");
            table.HasCheckConstraint(
                "CK_ImportBatches_Status",
                "[Status] IN ('PENDING', 'RUNNING', 'COMPLETED', 'FAILED', 'CANCELLED')");
        });

        // Cursor pagination on the import history.
        builder.HasIndex(b => new { b.CreatedAtUtc, b.Id })
            .HasDatabaseName("IX_ImportBatches_CreatedAtUtc_Id");

        // Resume after a crash: the worker picks up batches left in RUNNING or PENDING.
        builder.HasIndex(b => b.Status)
            .HasDatabaseName("IX_ImportBatches_Status");
    }
}

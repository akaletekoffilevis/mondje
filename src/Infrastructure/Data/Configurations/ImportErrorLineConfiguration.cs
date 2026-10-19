using Mondje.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mondje.Infrastructure.Data.Configurations;

public class ImportErrorLineConfiguration : IEntityTypeConfiguration<ImportErrorLine>
{
    public void Configure(EntityTypeBuilder<ImportErrorLine> builder)
    {
        builder.ToTable("ImportErrorLines");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnType("bigint")
            .ValueGeneratedOnAdd();

        builder.Property(e => e.ImportId)
            .HasColumnType("uniqueidentifier");

        builder.Property(e => e.Kind)
            .IsRequired()
            .HasColumnType("varchar(16)");

        builder.Property(e => e.Reason)
            .IsRequired()
            .HasColumnType("nvarchar(512)");

        builder.Property(e => e.RawLine)
            .IsRequired()
            .HasColumnType("nvarchar(1000)");

        builder.Property(e => e.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_ImportErrorLines_Kind",
                "[Kind] IN ('FORMAT', 'VALIDATION')");
        });

        // Paginated error report of one import, ordered by line number.
        builder.HasIndex(e => new { e.ImportId, e.LineNumber })
            .HasDatabaseName("IX_ImportErrorLines_ImportId_LineNumber");

        builder.HasOne<ImportBatch>()
            .WithMany()
            .HasForeignKey(e => e.ImportId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

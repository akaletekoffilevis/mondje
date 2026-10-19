using Mondje.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Mondje.Infrastructure.Data.Configurations;

public class TransactionTokenConfiguration : IEntityTypeConfiguration<TransactionToken>
{
    public void Configure(EntityTypeBuilder<TransactionToken> builder)
    {
        builder.ToTable("TransactionTokens");

        // Word first, then date: a search for one word reads its rows in exactly the
        // order the list needs (newest first), with no sort and no table scan.
        builder.HasKey(k => new { k.Token, k.CreatedAtUtc, k.TransactionId });

        builder.Property(k => k.Token)
            .HasColumnType("nvarchar(64)");

        builder.Property(k => k.CreatedAtUtc)
            .HasColumnType("datetime2(3)");

        builder.Property(k => k.TransactionId)
            .HasColumnType("char(36)");

        // Editing or hard-deleting a transaction (cancellation) looks its tokens up by
        // transaction; without this index every cascade would scan the whole word index.
        builder.HasIndex(k => k.TransactionId)
            .HasDatabaseName("IX_TransactionTokens_TransactionId");

        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(k => k.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

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

        // Deliberately no foreign key and no index on TransactionId: maintaining them
        // made a 1M-line import write hundreds of thousands of random index entries. The
        // words are removed by set-based statements instead (cancellation, edition), and
        // the word table stays a single clustered index that is written in key order.
    }
}

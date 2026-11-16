using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.ToTable("SavedCards");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.FinixPaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.Brand).HasMaxLength(64);
        builder.Property(c => c.Last4).IsRequired().HasMaxLength(4);
        builder.Property(c => c.Name).HasMaxLength(256);

        builder.HasIndex(c => c.OwnerId);
        builder.HasIndex(c => c.FinixPaymentInstrumentId).IsUnique();
    }
}

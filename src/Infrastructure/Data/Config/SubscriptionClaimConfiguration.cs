using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionClaimConfiguration : IEntityTypeConfiguration<SubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<SubscriptionClaim> builder)
    {
        // The primary key IS the claim: one subscribe claim per user, refused by the store on a second insert.
        builder.HasKey(c => c.CustomerReference);

        builder.Property(c => c.CustomerReference)
            .IsRequired()
            .HasMaxLength(256)
            .ValueGeneratedNever();

        builder.Property(c => c.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(c => c.PlanHandle)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.Status)
            .IsRequired();

        builder.Property(c => c.Version)
            .IsConcurrencyToken();
    }
}

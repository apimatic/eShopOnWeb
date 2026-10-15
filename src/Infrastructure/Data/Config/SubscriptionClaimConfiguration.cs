using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionClaimConfiguration : IEntityTypeConfiguration<SubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<SubscriptionClaim> builder)
    {
        // One claim per (user, plan); inserting it is what refuses a second concurrent subscribe.
        builder.HasKey(c => new { c.UserKey, c.PlanHandle });
        builder.Property(c => c.UserKey)
            .HasMaxLength(256)
            .ValueGeneratedNever();
        builder.Property(c => c.PlanHandle)
            .HasMaxLength(256)
            .ValueGeneratedNever();

        builder.Property(c => c.Reference)
            .IsRequired()
            .HasMaxLength(100);
        builder.HasIndex(c => c.Reference)
            .IsUnique();

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(c => c.Version)
            .IsConcurrencyToken();
    }
}

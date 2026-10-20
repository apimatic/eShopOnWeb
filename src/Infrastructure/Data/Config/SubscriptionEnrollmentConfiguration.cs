using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.ToTable("SubscriptionEnrollments");

        // "{customerReference}:{planHandle}" is the primary key: a second subscribe to the same plan
        // by the same user is refused by the store before it can reach the billing provider.
        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key)
            .HasMaxLength(BillingClaimColumns.KeyMaxLength)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(e => e.CustomerReference)
            .HasMaxLength(BillingClaimColumns.KeyMaxLength)
            .IsRequired();

        builder.Property(e => e.PlanHandle)
            .HasMaxLength(BillingClaimColumns.KeyMaxLength)
            .IsRequired();

        builder.Property(e => e.State)
            .HasConversion<string>()
            .HasMaxLength(BillingClaimColumns.StateMaxLength)
            .IsRequired();

        builder.Property(e => e.Version).IsConcurrencyToken();

        builder.HasIndex(e => e.CustomerReference);
    }
}

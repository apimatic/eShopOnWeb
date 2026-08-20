using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.ToTable("SubscriptionEnrollments");
        builder.Property(x => x.Id).UseHiLo("subscription_enrollment_hilo").IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ProductHandle).HasMaxLength(255).IsRequired();
        builder.Property(x => x.MaxioSubscriptionReference).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ProductName).HasMaxLength(255);
        builder.Property(x => x.BillingIntervalUnit).HasMaxLength(32);
        builder.Property(x => x.SubscriptionState).HasMaxLength(64);
        builder.Property(x => x.ProvisioningOwner).HasMaxLength(36);
        builder.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
        builder.HasIndex(x => x.MaxioSubscriptionReference).IsUnique();
    }
}

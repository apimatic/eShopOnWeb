using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // One enrollment per shopper: the primary key is what refuses a concurrent second subscribe claim
        // (enforced by SQL Server and by the in-memory provider alike).
        builder.HasKey(e => e.BuyerId);

        builder.Property(e => e.BuyerId)
            .HasMaxLength(256);

        builder.Property(e => e.PlanHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(e => e.SubscriptionReference)
            .IsUnique();

        builder.Property(e => e.Status)
            .IsRequired();
    }
}

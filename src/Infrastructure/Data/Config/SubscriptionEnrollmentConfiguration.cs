using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.ToTable("SubscriptionEnrollments");

        builder.Property(e => e.SubscriberKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.CustomerReference)
            .HasMaxLength(255);

        builder.Property(e => e.ProductHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.State)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(e => new { e.SubscriberKey, e.ProductHandle })
            .IsUnique()
            .HasDatabaseName("IX_SubscriptionEnrollments_SubscriberKey_ProductHandle");
    }
}

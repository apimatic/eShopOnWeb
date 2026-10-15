using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.HasKey(e => new { e.BuyerId, e.PlanHandle });
        builder.Property(e => e.BuyerId).HasMaxLength(256);
        builder.Property(e => e.PlanHandle).HasMaxLength(256);
        builder.Property(e => e.Reference).IsRequired().HasMaxLength(256);
    }
}

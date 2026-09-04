using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.PlanHandle).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SubscriptionReference).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.PlanHandle }).IsUnique();
        builder.HasIndex(x => x.SubscriptionReference).IsUnique();
    }
}

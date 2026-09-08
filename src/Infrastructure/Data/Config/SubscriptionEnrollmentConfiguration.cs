using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.ToTable("SubscriptionEnrollments");

        builder.Property(enrollment => enrollment.UserId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(enrollment => enrollment.PlanHandle)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(enrollment => new { enrollment.UserId, enrollment.PlanHandle })
            .IsUnique();

        builder.Property(enrollment => enrollment.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(enrollment => enrollment.MaxioSubscriptionId);

        builder.Property(enrollment => enrollment.CreatedAt)
            .IsRequired();
    }
}

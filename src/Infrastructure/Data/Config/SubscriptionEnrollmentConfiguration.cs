using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.Infrastructure.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.ToTable("SubscriptionEnrollments");
        builder.HasKey(enrollment => enrollment.Id);
        builder.HasIndex(enrollment => new { enrollment.UserId, enrollment.ProductHandle }).IsUnique();
        builder.HasIndex(enrollment => enrollment.SubscriptionReference).IsUnique();
        builder.HasIndex(enrollment => enrollment.MaxioSubscriptionId).IsUnique();
        builder.Property(enrollment => enrollment.UserId).HasMaxLength(450).IsRequired();
        builder.Property(enrollment => enrollment.ProductHandle).HasMaxLength(255).IsRequired();
        builder.Property(enrollment => enrollment.SubscriptionReference).HasMaxLength(450).IsRequired();
        builder.Property(enrollment => enrollment.Status).HasMaxLength(32).IsRequired();
        builder.Property(enrollment => enrollment.UpdatedAt).IsRequired();
    }
}

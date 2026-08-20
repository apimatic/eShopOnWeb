using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.ToTable("SubscriptionEnrollments");
        builder.HasKey(enrollment => enrollment.Id);
        builder.Property(enrollment => enrollment.Id).UseIdentityColumn();
        builder.Property(enrollment => enrollment.UserId).HasMaxLength(450).IsRequired();
        builder.Property(enrollment => enrollment.ProductHandle).HasMaxLength(255).IsRequired();
        builder.Property(enrollment => enrollment.CustomerReference).HasMaxLength(255).IsRequired();
        builder.Property(enrollment => enrollment.SubscriptionReference).HasMaxLength(255).IsRequired();
        builder.HasIndex(enrollment => new { enrollment.UserId, enrollment.ProductHandle }).IsUnique();
        builder.HasIndex(enrollment => enrollment.CustomerReference);
        builder.HasIndex(enrollment => enrollment.SubscriptionReference).IsUnique();
    }
}

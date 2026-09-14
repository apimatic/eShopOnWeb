using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.ToTable("SubscriptionEnrollments");
        builder.HasKey(enrollment => enrollment.Id);
        builder.Property(enrollment => enrollment.UserId).IsRequired().HasMaxLength(450);
        builder.Property(enrollment => enrollment.ProductHandle).IsRequired().HasMaxLength(100);
        builder.HasIndex(enrollment => new { enrollment.UserId, enrollment.ProductHandle }).IsUnique();
    }
}

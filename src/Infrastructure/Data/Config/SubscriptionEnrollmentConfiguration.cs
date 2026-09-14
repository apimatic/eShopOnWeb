using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.ToTable("SubscriptionEnrollments");

        builder.Property(enrollment => enrollment.UserId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(enrollment => enrollment.ProductHandle)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(enrollment => enrollment.CustomerReference)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(enrollment => enrollment.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(400);

        builder.Property(enrollment => enrollment.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(enrollment => enrollment.LastError)
            .HasMaxLength(1000);

        builder.Property(enrollment => enrollment.Version)
            .IsConcurrencyToken();

        builder.HasIndex(enrollment => new { enrollment.UserId, enrollment.ProductHandle })
            .IsUnique();

        builder.HasIndex(enrollment => enrollment.SubscriptionReference)
            .IsUnique();
    }
}

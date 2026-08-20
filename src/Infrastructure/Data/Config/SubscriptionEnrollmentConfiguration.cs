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
            .HasMaxLength(128);

        builder.Property(enrollment => enrollment.ProductHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(enrollment => enrollment.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(400);

        builder.Property(enrollment => enrollment.OperationId)
            .IsRequired()
            .HasMaxLength(36)
            .IsConcurrencyToken();

        builder.HasIndex(enrollment => new { enrollment.UserId, enrollment.ProductHandle })
            .IsUnique();

        builder.HasIndex(enrollment => enrollment.SubscriptionReference)
            .IsUnique();

        builder.HasIndex(enrollment => enrollment.MaxioSubscriptionId)
            .IsUnique()
            .HasFilter("[MaxioSubscriptionId] IS NOT NULL");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.ToTable("UserSubscriptions");

        builder.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(x => x.CustomerReference).IsRequired().HasMaxLength(100);
        builder.Property(x => x.SubscriptionReference).IsRequired().HasMaxLength(100);
        builder.Property(x => x.EnrollmentToken)
            .IsRequired()
            .HasMaxLength(36)
            .IsConcurrencyToken();

        builder.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
        builder.HasIndex(x => x.MaxioSubscriptionId)
            .IsUnique()
            .HasFilter("[MaxioSubscriptionId] IS NOT NULL");
        builder.HasIndex(x => x.SubscriptionReference).IsUnique();
    }
}

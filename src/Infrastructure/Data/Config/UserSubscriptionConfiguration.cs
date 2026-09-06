using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.HasKey(us => us.Id);

        builder.Property(us => us.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(us => us.ProductHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(us => us.CreatedAt)
            .IsRequired();

        builder.HasIndex(us => new { us.UserId, us.MaxioSubscriptionId })
            .IsUnique()
            .HasDatabaseName("IX_UserSubscription_UserId_MaxioSubscriptionId");

        builder.HasIndex(us => us.MaxioCustomerId)
            .HasDatabaseName("IX_UserSubscription_MaxioCustomerId");
    }
}

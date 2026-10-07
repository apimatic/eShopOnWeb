using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.ToTable("UserSubscriptions");

        builder.Property(s => s.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(s => s.UserEmail)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.PlanHandle)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(s => s.PlanName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.State)
            .IsRequired()
            .HasMaxLength(64);

        // One subscription record per user per plan keeps double-clicks
        // idempotent at the persistence layer as well as the service layer.
        builder.HasIndex(s => new { s.UserId, s.PlanHandle })
            .IsUnique();
    }
}

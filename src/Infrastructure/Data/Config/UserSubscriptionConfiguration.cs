using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.Property(s => s.UserId)
            .IsRequired()
            .HasMaxLength(256);
        builder.Property(s => s.UserName)
            .IsRequired()
            .HasMaxLength(256);
        builder.Property(s => s.ProductHandle)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(s => s.ProductName)
            .IsRequired()
            .HasMaxLength(256);
        builder.Property(s => s.Currency)
            .IsRequired()
            .HasMaxLength(3);
        builder.Property(s => s.State)
            .IsRequired()
            .HasMaxLength(50);
        // One subscription record per plan per user; enforced by the database
        // so a double-click can never persist two subscriptions.
        builder.HasIndex(s => new { s.UserId, s.ProductHandle })
            .IsUnique();
        builder.HasIndex(s => s.UserId);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.UserId).IsRequired();
        builder.Property(s => s.MaxioSubscriptionId).IsRequired();
        builder.Property(s => s.MaxioProductId).IsRequired();
        builder.Property(s => s.ProductHandle).IsRequired().HasMaxLength(256);
        builder.Property(s => s.State).IsRequired().HasMaxLength(50);
        builder.Property(s => s.CurrentPrice).HasPrecision(18, 2);
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => new { s.UserId, s.MaxioSubscriptionId }).IsUnique();
    }
}

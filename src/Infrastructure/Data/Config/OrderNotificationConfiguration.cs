using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageSid).HasMaxLength(64);
        builder.Property(x => x.ProviderStatus).IsRequired().HasMaxLength(32);
        builder.Property(x => x.ProviderDirection).HasMaxLength(32);
        builder.Property(x => x.ProviderErrorMessage).HasMaxLength(512);
        builder.Property(x => x.ProviderDateCreated).HasMaxLength(64);
        builder.Property(x => x.ProviderDateSent).HasMaxLength(64);
        builder.Property(x => x.ProviderDateUpdated).HasMaxLength(64);
        builder.HasIndex(x => x.ProviderMessageSid).IsUnique();
        builder.HasIndex(x => new { x.OrderId, x.CreatedAt });
        builder.HasIndex(x => new { x.BuyerId, x.CreatedAt });
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

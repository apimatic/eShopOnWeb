using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Body).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageSid).HasMaxLength(64);
        builder.Property(x => x.ProviderStatus).IsRequired().HasMaxLength(64);
        builder.HasIndex(x => x.ProviderMessageSid).IsUnique().HasFilter("[ProviderMessageSid] IS NOT NULL");
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => new { x.ContactNumberId, x.Kind, x.ProviderStatus });
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ContactNumber>().WithMany().HasForeignKey(x => x.ContactNumberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderNotification>().WithMany().HasForeignKey(x => x.SourceNotificationId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

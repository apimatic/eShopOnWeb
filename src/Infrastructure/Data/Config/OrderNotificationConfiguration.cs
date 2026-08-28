using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Body).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageSid).HasMaxLength(34);
        builder.Property(x => x.ProviderStatus).IsRequired().HasMaxLength(64);
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.ProviderMessageSid).IsUnique().HasFilter("[ProviderMessageSid] IS NOT NULL");
        builder.HasOne<ContactNumber>()
            .WithMany()
            .HasForeignKey(x => x.ContactNumberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

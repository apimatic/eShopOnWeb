using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageSid).HasMaxLength(34);
        builder.Property(x => x.ProviderStatus).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.ContactNumberId);
        builder.HasIndex(x => x.ProviderMessageSid)
            .IsUnique()
            .HasFilter("[ProviderMessageSid] IS NOT NULL");
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ContactNumber>().WithMany().HasForeignKey(x => x.ContactNumberId).OnDelete(DeleteBehavior.Restrict);
    }
}

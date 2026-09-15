using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.Content).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageSid).HasMaxLength(34);
        builder.Property(x => x.ProviderStatus).IsRequired().HasMaxLength(40);
        builder.Property(x => x.ProviderErrorMessage).HasMaxLength(512);
        builder.HasIndex(x => x.ProviderMessageSid).IsUnique().HasFilter("[ProviderMessageSid] IS NOT NULL");
        builder.HasIndex(x => x.OrderId);
        builder.HasOne<Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ContactNumber>()
            .WithMany()
            .HasForeignKey(x => x.ContactNumberId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageId).HasMaxLength(34);
        builder.Property(x => x.ProviderStatus).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProviderErrorMessage).HasMaxLength(2048);
        builder.HasIndex(x => x.ProviderMessageId).IsUnique().HasFilter("[ProviderMessageId] IS NOT NULL");

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ContactNumber>()
            .WithMany()
            .HasForeignKey(x => x.ContactNumberId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<OrderNotification>()
            .WithMany()
            .HasForeignKey(x => x.SourceNotificationId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

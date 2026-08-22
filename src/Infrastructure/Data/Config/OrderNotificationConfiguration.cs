using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(n => n.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.DestinationPhoneNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(n => n.Body)
            .HasMaxLength(1600);

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(34);

        builder.Property(n => n.ProviderStatus)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.ContentRedacted)
            .IsRequired();

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.ProviderMessageSid);
    }
}

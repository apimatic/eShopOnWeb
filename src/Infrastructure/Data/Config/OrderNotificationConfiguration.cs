using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(n => n.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.ToPhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.MessageBody)
            .HasMaxLength(1600);

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.Status)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.Property(n => n.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.BuyerId);
        builder.HasIndex(n => n.IdempotencyKey);
    }
}

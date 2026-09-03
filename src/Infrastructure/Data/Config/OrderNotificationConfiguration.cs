using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(n => n.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.Kind)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(n => n.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(n => n.Body)
            .HasMaxLength(1600);

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.ProviderStatus)
            .HasMaxLength(64);

        builder.Property(n => n.ProviderErrorMessage)
            .HasMaxLength(1024);

        builder.Property(n => n.ResendIdempotencyKey)
            .HasMaxLength(256);

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(n => n.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ContactNumber>()
            .WithMany()
            .HasForeignKey(n => n.ContactNumberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<OrderNotification>()
            .WithMany()
            .HasForeignKey(n => n.SourceNotificationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(n => new { n.OrderId, n.BuyerId });
        builder.HasIndex(n => new { n.ContactNumberId, n.ScheduledFor, n.CancellationCompletedAt });
        builder.HasIndex(n => n.CreatedAt);

        builder.HasIndex(n => n.ProviderMessageSid)
            .IsUnique()
            .HasFilter("[ProviderMessageSid] IS NOT NULL");

        builder.HasIndex(n => new { n.SourceNotificationId, n.ResendIdempotencyKey })
            .IsUnique()
            .HasFilter("[SourceNotificationId] IS NOT NULL AND [ResendIdempotencyKey] IS NOT NULL");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

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

        builder.Property(n => n.Type)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(n => n.Status)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(n => n.MessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.ErrorMessage)
            .HasMaxLength(512);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.BuyerId);
        builder.HasIndex(n => n.MessageSid);
        // Enforces resend idempotency at the store for non-null keys only (the vast majority of
        // notifications carry no key). The filter keeps multiple NULLs legal on SQL Server.
        builder.HasIndex(n => n.IdempotencyKey)
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL");
    }
}

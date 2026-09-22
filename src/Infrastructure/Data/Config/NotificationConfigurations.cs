using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.Property(c => c.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.Value).IsRequired().HasMaxLength(32);
        builder.HasIndex(c => c.OwnerId);
    }
}

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(n => n.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(n => n.ToNumber).IsRequired().HasMaxLength(32);
        builder.Property(n => n.Body).HasMaxLength(1600);
        builder.Property(n => n.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(n => n.Status).IsRequired().HasMaxLength(32);
        builder.Property(n => n.ProviderMessageSid).HasMaxLength(64);
        builder.Property(n => n.ErrorMessage).HasMaxLength(1024);
        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.OwnerId);
        builder.HasIndex(n => n.ProviderMessageSid);
    }
}

public class NotificationResendClaimConfiguration : IEntityTypeConfiguration<NotificationResendClaim>
{
    public void Configure(EntityTypeBuilder<NotificationResendClaim> builder)
    {
        // The caller-supplied idempotency key IS the primary key — this is what rejects a duplicate
        // resend request (enforced by SQL Server and by the EF in-memory keyed store alike).
        builder.HasKey(c => c.IdempotencyKey);
        builder.Property(c => c.IdempotencyKey).HasMaxLength(128);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Number).HasMaxLength(32).IsRequired();
        builder.Ignore(x => x.IsActive);
        builder.HasIndex(x => new { x.BuyerId, x.Number });
    }
}

public sealed class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageSid).HasMaxLength(64);
        builder.Property(x => x.ProviderStatus).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProviderErrorMessage).HasMaxLength(512);
        builder.HasIndex(x => x.ProviderMessageSid).IsUnique();
        builder.HasIndex(x => new { x.OrderId, x.CreatedAt });
        builder.HasOne<Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class NotificationResendClaimConfiguration : IEntityTypeConfiguration<NotificationResendClaim>
{
    public void Configure(EntityTypeBuilder<NotificationResendClaim> builder)
    {
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.SourceNotificationId, x.IdempotencyKey }).IsUnique();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(n => n.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(n => n.ToNumber).IsRequired().HasMaxLength(32);
        builder.Property(n => n.Body).HasMaxLength(1600);
        builder.Property(n => n.ProviderMessageSid).HasMaxLength(64);
        builder.Property(n => n.ProviderStatus).HasMaxLength(32);
        builder.Property(n => n.ProviderErrorMessage).HasMaxLength(512);
        builder.Property(n => n.IdempotencyKey).HasMaxLength(128);
        builder.Property(n => n.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(n => n.SendState).HasConversion<string>().HasMaxLength(32);

        // Resend idempotency: at most one notification per caller-supplied key. Filtered so the many
        // notifications with no key (all non-resend messages) are unconstrained.
        builder.HasIndex(n => n.IdempotencyKey)
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL");

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.OwnerId);
    }
}

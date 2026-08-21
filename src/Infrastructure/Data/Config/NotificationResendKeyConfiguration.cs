using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationResendKeyConfiguration : IEntityTypeConfiguration<NotificationResendKey>
{
    public void Configure(EntityTypeBuilder<NotificationResendKey> builder)
    {
        builder.ToTable("NotificationResendKeys");

        builder.Property(k => k.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(k => new { k.SourceNotificationId, k.IdempotencyKey })
            .IsUnique();
    }
}

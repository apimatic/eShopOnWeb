using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationResendKeyConfiguration : IEntityTypeConfiguration<NotificationResendKey>
{
    public void Configure(EntityTypeBuilder<NotificationResendKey> builder)
    {
        builder.Property(k => k.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(k => new { k.NotificationId, k.IdempotencyKey })
            .IsUnique();
    }
}

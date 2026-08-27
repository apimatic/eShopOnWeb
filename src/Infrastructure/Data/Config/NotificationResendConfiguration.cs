using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class NotificationResendConfiguration : IEntityTypeConfiguration<NotificationResend>
{
    public void Configure(EntityTypeBuilder<NotificationResend> builder)
    {
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(64);
        builder.HasIndex(x => new { x.SourceNotificationId, x.IdempotencyKey }).IsUnique();
    }
}

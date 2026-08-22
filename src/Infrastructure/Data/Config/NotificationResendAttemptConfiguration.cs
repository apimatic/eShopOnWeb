using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationResendAttemptConfiguration : IEntityTypeConfiguration<NotificationResendAttempt>
{
    public void Configure(EntityTypeBuilder<NotificationResendAttempt> builder)
    {
        builder.ToTable("NotificationResendAttempts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(a => new { a.SourceNotificationId, a.IdempotencyKey }).IsUnique();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class NotificationResendConfiguration : IEntityTypeConfiguration<NotificationResend>
{
    public void Configure(EntityTypeBuilder<NotificationResend> builder)
    {
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
    }
}

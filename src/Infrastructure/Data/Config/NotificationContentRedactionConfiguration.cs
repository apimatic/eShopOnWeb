using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationContentRedactionConfiguration : IEntityTypeConfiguration<NotificationContentRedaction>
{
    public void Configure(EntityTypeBuilder<NotificationContentRedaction> builder)
    {
        builder.HasIndex(r => r.NotificationId).IsUnique();
    }
}

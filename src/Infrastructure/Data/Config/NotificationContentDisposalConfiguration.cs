using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationContentDisposalConfiguration : IEntityTypeConfiguration<NotificationContentDisposal>
{
    public void Configure(EntityTypeBuilder<NotificationContentDisposal> builder)
    {
        builder.HasIndex(d => d.NotificationId);
    }
}

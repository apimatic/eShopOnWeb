using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderStatusRecordConfiguration : IEntityTypeConfiguration<OrderStatusRecord>
{
    public void Configure(EntityTypeBuilder<OrderStatusRecord> builder)
    {
        builder.Property(o => o.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(o => o.State)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(o => o.OrderId).IsUnique();
        builder.HasIndex(o => o.BuyerId);
    }
}

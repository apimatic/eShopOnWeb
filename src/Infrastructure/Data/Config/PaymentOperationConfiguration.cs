using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentOperationConfiguration : IEntityTypeConfiguration<PaymentOperation>
{
    public void Configure(EntityTypeBuilder<PaymentOperation> builder)
    {
        builder.HasKey(o => o.Key);
        builder.Property(o => o.Key).HasMaxLength(200);
        builder.Property(o => o.Kind).IsRequired().HasMaxLength(32);
        builder.Property(o => o.BuyerId).HasMaxLength(256);
        builder.Property(o => o.ResultId).HasMaxLength(64);
        builder.Property(o => o.State).IsRequired().HasMaxLength(32);
        builder.Property(o => o.Error).HasMaxLength(1000);
        builder.HasIndex(o => o.OrderId);
    }
}

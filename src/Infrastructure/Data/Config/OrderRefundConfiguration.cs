using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.ToTable("OrderRefunds");
        builder.Property<int>("OrderPaymentId").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ProviderRefundId).HasMaxLength(128);
        builder.HasIndex(x => x.ProviderRefundId).IsUnique();
        builder.Property(x => x.Status).HasMaxLength(64).IsRequired();
        builder.Property(x => x.StatusReason).HasMaxLength(256);
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.HasIndex("OrderPaymentId", nameof(OrderRefund.IdempotencyKey)).IsUnique();
        builder.Ignore(x => x.CountsAgainstCapture);
    }
}

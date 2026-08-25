using System.Text.Json;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.Property(p => p.PaymentStatus)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(p => p.PayPalOrderId)
            .HasMaxLength(50);

        builder.Property(p => p.AuthorizationId)
            .HasMaxLength(50);

        builder.Property(p => p.CaptureId)
            .HasMaxLength(50);

        builder.Property(p => p.CapturedAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.PayPalFeeAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.NetAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.TotalRefundedAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.RefundIds)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());
    }
}

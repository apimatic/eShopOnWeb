using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");

        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.OrderAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.InvoiceId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.PayPalOrderId).HasMaxLength(32);
        builder.Property(x => x.FundingBrand).HasMaxLength(32);
        builder.Property(x => x.FundingLastDigits).HasMaxLength(4);
        builder.Property(x => x.CaptureId).HasMaxLength(32);
        builder.Property(x => x.CaptureStatus).HasMaxLength(32);
        builder.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        builder.HasIndex(x => x.PayPalOrderId).IsUnique().HasFilter("[PayPalOrderId] IS NOT NULL");
        builder.HasIndex(x => x.InvoiceId).IsUnique();
        builder.HasIndex(x => x.CaptureId).IsUnique().HasFilter("[CaptureId] IS NOT NULL");

        builder.Ignore(x => x.CurrentAuthorization);
        builder.Ignore(x => x.RefundedAmount);

        var authorizations = builder.Metadata.FindNavigation(nameof(OrderPayment.Authorizations));
        authorizations?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.Authorizations)
            .WithOne()
            .HasForeignKey(x => x.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        var refunds = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.Refunds)
            .WithOne()
            .HasForeignKey(x => x.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

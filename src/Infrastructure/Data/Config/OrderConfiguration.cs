using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        var navigation = builder.Metadata.FindNavigation(nameof(Order.OrderItems));

        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        var refundsNavigation = builder.Metadata.FindNavigation(nameof(Order.Refunds));

        refundsNavigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(b => b.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.OwnsOne(o => o.ShipToAddress, a =>
        {
            a.WithOwner();

            a.Property(a => a.ZipCode)
                .HasMaxLength(18)
                .IsRequired();

            a.Property(a => a.Street)
                .HasMaxLength(180)
                .IsRequired();

            a.Property(a => a.State)
                .HasMaxLength(60);

            a.Property(a => a.Country)
                .HasMaxLength(90)
                .IsRequired();

            a.Property(a => a.City)
                .HasMaxLength(100)
                .IsRequired();
        });

        builder.Navigation(x => x.ShipToAddress).IsRequired();

        builder.OwnsMany(o => o.Refunds, refund =>
        {
            refund.WithOwner().HasForeignKey("OrderId");
            refund.ToTable("OrderPaymentRefunds");
            refund.Property<int>("Id");
            refund.HasKey("Id");

            refund.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
            refund.Property(r => r.RefundReference).HasMaxLength(160);
            refund.Property(r => r.ProviderRefundId).HasMaxLength(64).IsRequired();
            refund.Property(r => r.ProviderCaptureId).HasMaxLength(64);
            refund.Property(r => r.CurrencyCode).HasMaxLength(3);
            refund.Property(r => r.Status).HasMaxLength(32);

            refund.Property(r => r.Amount).HasPrecision(18, 2);
            refund.Property(r => r.TotalRefundedAmount).HasPrecision(18, 2);

            refund.Ignore(r => r.ConsumesCaptureAmount);
        });

        builder.OwnsOne(o => o.Payment, payment =>
        {
            payment.WithOwner();
            payment.ToTable("OrderPayments");

            payment.Property(p => p.ProviderName).HasMaxLength(32).IsRequired();
            payment.Property(p => p.ProviderOrderId).HasMaxLength(64).IsRequired();
            payment.Property(p => p.AuthorizationId).HasMaxLength(64);
            payment.Property(p => p.AuthorizationStatus).HasMaxLength(32);
            payment.Property(p => p.CurrencyCode).HasMaxLength(3);
            payment.Property(p => p.NetworkTransactionReference).HasMaxLength(128);
            payment.Property(p => p.UsedVaultTokenId).HasMaxLength(64);
            payment.Property(p => p.InvoiceReference).HasMaxLength(128);
            payment.Property(p => p.CaptureId).HasMaxLength(64);
            payment.Property(p => p.CaptureStatus).HasMaxLength(32);

            payment.Property(p => p.AuthorizedAmount).HasPrecision(18, 2);
            payment.Property(p => p.CapturedAmount).HasPrecision(18, 2);
            payment.Property(p => p.FeeAmount).HasPrecision(18, 2);
            payment.Property(p => p.NetAmount).HasPrecision(18, 2);

            payment.Ignore(p => p.IsCaptured);
            payment.Ignore(p => p.IsVoided);
            payment.Ignore(p => p.AuthorizationExpired);
            payment.Ignore(p => p.HasPendingAuthorizationToRecover);
        });

        builder.Navigation(x => x.Payment).IsRequired(false);
    }
}

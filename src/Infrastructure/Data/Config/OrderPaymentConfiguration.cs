using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");
        builder.Property(x => x.State).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.InvoiceId).HasMaxLength(127).IsRequired();
        builder.Property(x => x.ReferenceId).HasMaxLength(127).IsRequired();
        builder.HasIndex(x => x.InvoiceId).IsUnique();
        builder.HasIndex(x => x.ReferenceId).IsUnique();
        builder.Property(x => x.PayPalOrderId).HasMaxLength(32);
        builder.Property(x => x.PayPalOrderStatus).HasMaxLength(32);
        builder.Property(x => x.AuthorizationId).HasMaxLength(32);
        builder.Property(x => x.AuthorizationStatus).HasMaxLength(32);
        builder.Property(x => x.AuthorizedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CaptureId).HasMaxLength(32);
        builder.Property(x => x.CaptureStatus).HasMaxLength(32);
        builder.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.FailureCode).HasMaxLength(128);
        builder.Property(x => x.FailureMessage).HasMaxLength(1000);
        builder.Ignore(x => x.RefundedAmount);

        var refunds = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.Refunds)
            .WithOne()
            .HasForeignKey("OrderPaymentId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.State).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(p => p.OrderId).IsUnique();

        // Refunds are children of the Payment aggregate, accessed through the private field.
        // Modelled as a first-class one-to-many (like Order -> OrderItem) which the EF Core
        // in-memory provider tracks and persists reliably when new children are added.
        var refunds = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3);
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(256);
        builder.Property(x => x.PayPalRefundId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Status).HasMaxLength(30);
    }
}

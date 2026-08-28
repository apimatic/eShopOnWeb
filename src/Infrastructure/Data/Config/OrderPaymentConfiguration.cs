using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");
        builder.HasOne<Order>()
            .WithOne(x => x.Payment)
            .HasForeignKey<OrderPayment>("OrderId")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property<int>("OrderId").IsRequired();
        builder.HasIndex("OrderId").IsUnique();

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.OperationId).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.ProviderOrderId).HasMaxLength(128);
        builder.HasIndex(x => x.ProviderOrderId).IsUnique();
        builder.Property(x => x.ProviderOrderStatus).HasMaxLength(64);
        builder.Property(x => x.AuthorizationId).HasMaxLength(128);
        builder.HasIndex(x => x.AuthorizationId).IsUnique();
        builder.Property(x => x.AuthorizationStatus).HasMaxLength(64);
        builder.Property(x => x.AuthorizationStatusReason).HasMaxLength(256);
        builder.Property(x => x.AuthorizedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CaptureId).HasMaxLength(128);
        builder.HasIndex(x => x.CaptureId).IsUnique();
        builder.Property(x => x.CaptureStatus).HasMaxLength(64);
        builder.Property(x => x.CaptureStatusReason).HasMaxLength(256);
        builder.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Version).IsConcurrencyToken();

        var refunds = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.Refunds)
            .WithOne()
            .HasForeignKey("OrderPaymentId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

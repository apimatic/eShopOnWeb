using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.Property(method => method.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(method => method.FinixPaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(method => method.Brand)
            .HasMaxLength(64);

        builder.Property(method => method.LastFour)
            .HasMaxLength(4);

        builder.Property(method => method.Name)
            .HasMaxLength(256);

        builder.HasIndex(method => method.OwnerId);
        builder.HasIndex(method => method.FinixPaymentInstrumentId);
    }
}

public class PaymentCustomerConfiguration : IEntityTypeConfiguration<PaymentCustomer>
{
    public void Configure(EntityTypeBuilder<PaymentCustomer> builder)
    {
        builder.ToTable("PaymentCustomers");

        builder.Property(customer => customer.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(customer => customer.FinixIdentityId)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(customer => customer.OwnerId).IsUnique();
    }
}

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.ToTable("OrderRefunds");

        builder.Property(refund => refund.FinixRefundId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(refund => refund.Amount)
            .HasColumnType("decimal(18,2)");

        builder.Property(refund => refund.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(refund => refund.State)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(refund => refund.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(refund => refund.OrderId);
        builder.HasIndex(refund => refund.FinixRefundId);
        builder.HasIndex(refund => new { refund.OrderId, refund.IdempotencyKey }).IsUnique();
    }
}

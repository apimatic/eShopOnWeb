using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentCustomerConfiguration : IEntityTypeConfiguration<PaymentCustomer>
{
    public void Configure(EntityTypeBuilder<PaymentCustomer> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.StaxCustomerId).IsRequired().HasMaxLength(64);
        builder.HasIndex(p => p.BuyerId).IsUnique();
    }
}

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.StaxPaymentMethodId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Method).IsRequired().HasMaxLength(16);
        builder.Property(p => p.Brand).HasMaxLength(64);
        builder.Property(p => p.Last4).HasMaxLength(8);
        builder.Property(p => p.Nickname).HasMaxLength(256);
        builder.HasIndex(p => p.StaxPaymentMethodId).IsUnique();
        builder.HasIndex(p => p.BuyerId);
    }
}

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.StaxPaymentMethodId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Method).IsRequired().HasMaxLength(16);
        builder.Property(p => p.Status).IsRequired().HasMaxLength(16);
        builder.Property(p => p.RefundStatus).IsRequired().HasMaxLength(16);
        builder.Property(p => p.StaxTransactionId).HasMaxLength(64);
        builder.Property(p => p.FailureReason).HasMaxLength(1000);
        builder.Property(p => p.RefundMessage).HasMaxLength(1000);
        builder.Property(p => p.RefundRequestedBy).HasMaxLength(256);
        builder.Property(p => p.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Fee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Total).HasColumnType("decimal(18,2)");
        builder.Property(p => p.RefundAmount).HasColumnType("decimal(18,2)");
        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.BuyerId);
    }
}

public class InvoiceScheduleRecordConfiguration : IEntityTypeConfiguration<InvoiceScheduleRecord>
{
    public void Configure(EntityTypeBuilder<InvoiceScheduleRecord> builder)
    {
        builder.Property(p => p.StaxScheduleId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.StaxCustomerId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.CustomerEmail).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Company).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.FirstInvoiceDate).HasColumnType("date");
        builder.HasIndex(p => p.StaxScheduleId).IsUnique();
    }
}

public class SellerRecordConfiguration : IEntityTypeConfiguration<SellerRecord>
{
    public void Configure(EntityTypeBuilder<SellerRecord> builder)
    {
        builder.Property(p => p.StaxMerchantId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Company).IsRequired().HasMaxLength(256);
        builder.Property(p => p.ContactName).IsRequired().HasMaxLength(256);
        builder.Property(p => p.ContactEmail).IsRequired().HasMaxLength(256);
    }
}

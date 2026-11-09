using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasKey(p => p.OrderId);
        builder.Property(p => p.OrderId).ValueGeneratedNever();
        builder.Property(p => p.BuyerEmail).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Method).IsRequired().HasMaxLength(10);
        builder.Property(p => p.PaymentMethodId).IsRequired().HasMaxLength(50);
        builder.Property(p => p.StaxCustomerId).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Fee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Total).HasColumnType("decimal(18,2)");
        builder.Property(p => p.StaxTransactionId).HasMaxLength(50);
        builder.Property(p => p.RefundTransactionId).HasMaxLength(50);
        builder.Property(p => p.FailureReason).HasMaxLength(500);
        builder.Property(p => p.RefundNote).HasMaxLength(500);
        builder.HasIndex(p => p.BuyerEmail);
    }
}

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasMaxLength(50).ValueGeneratedNever();
        builder.Property(p => p.BuyerEmail).IsRequired().HasMaxLength(256);
        builder.Property(p => p.StaxCustomerId).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Type).IsRequired().HasMaxLength(10);
        builder.Property(p => p.Brand).HasMaxLength(30);
        builder.Property(p => p.Last4).HasMaxLength(4);
        builder.HasIndex(p => p.BuyerEmail);
    }
}

public class StaxCustomerLinkConfiguration : IEntityTypeConfiguration<StaxCustomerLink>
{
    public void Configure(EntityTypeBuilder<StaxCustomerLink> builder)
    {
        builder.HasKey(p => p.BuyerEmail);
        builder.Property(p => p.BuyerEmail).HasMaxLength(256);
        builder.Property(p => p.StaxCustomerId).IsRequired().HasMaxLength(50);
    }
}

public class InvoiceScheduleRecordConfiguration : IEntityTypeConfiguration<InvoiceScheduleRecord>
{
    public void Configure(EntityTypeBuilder<InvoiceScheduleRecord> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasMaxLength(50).ValueGeneratedNever();
        builder.Property(p => p.CustomerEmail).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Company).IsRequired().HasMaxLength(200);
        builder.Property(p => p.StaxCustomerId).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.FirstInvoiceDate).HasColumnType("date");
    }
}

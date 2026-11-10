using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> builder)
    {
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.Type).IsRequired().HasMaxLength(20);
        builder.Property(c => c.ItemId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.AccessToken).IsRequired().HasMaxLength(200);
        builder.Property(c => c.InstitutionId).HasMaxLength(50);
        builder.Property(c => c.InstitutionName).HasMaxLength(200);
        builder.Property(c => c.AccountId).HasMaxLength(100);
        builder.Property(c => c.AccountName).HasMaxLength(200);
        builder.Property(c => c.AccountMask).HasMaxLength(20);
        builder.Property(c => c.TransactionsCursor).HasMaxLength(500);
        builder.HasIndex(c => c.BuyerId);
        builder.HasIndex(c => c.ItemId).IsUnique();
    }
}

public class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.Property(t => t.TransactionId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.AccountId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(500);
        builder.Property(t => t.CurrencyCode).HasMaxLength(10);
        builder.Property(t => t.Amount).HasColumnType("decimal(18,4)");
        builder.HasIndex(t => new { t.ConnectionId, t.TransactionId }).IsUnique();
    }
}

public class BankCheckoutConfiguration : IEntityTypeConfiguration<BankCheckout>
{
    public void Configure(EntityTypeBuilder<BankCheckout> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasMaxLength(50);
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.Status).IsRequired().HasMaxLength(20);
        builder.Property(c => c.ShippingCost).HasColumnType("decimal(18,2)");
        builder.HasIndex(c => c.ConnectionId);
    }
}

public class BankPaymentConfiguration : IEntityTypeConfiguration<BankPayment>
{
    public void Configure(EntityTypeBuilder<BankPayment> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.CheckoutId).IsRequired().HasMaxLength(50);
        builder.Property(p => p.TransferId).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Speed).IsRequired().HasMaxLength(20);
        builder.Property(p => p.Status).IsRequired().HasMaxLength(20);
        builder.Property(p => p.RefundId).HasMaxLength(100);
        builder.Property(p => p.ItemsTotal).HasColumnType("decimal(18,2)");
        builder.Property(p => p.ShippingCost).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Fee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.CheckoutId).IsUnique();
    }
}

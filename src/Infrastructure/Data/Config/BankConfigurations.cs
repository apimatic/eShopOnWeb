using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> builder)
    {
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.ItemId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.AccessToken).IsRequired().HasMaxLength(200);
        builder.Property(c => c.InstitutionId).HasMaxLength(50);
        builder.Property(c => c.InstitutionName).HasMaxLength(200);
        builder.Property(c => c.TransactionsCursor).HasMaxLength(2000);
        builder.HasIndex(c => c.BuyerId);
        builder.HasIndex(c => c.ItemId).IsUnique();
    }
}

public class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.Property(t => t.PlaidTransactionId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.AccountId).HasMaxLength(100);
        builder.Property(t => t.Name).HasMaxLength(500);
        builder.Property(t => t.IsoCurrencyCode).HasMaxLength(10);
        builder.Property(t => t.Amount).HasColumnType("decimal(18,2)");
        builder.Property(t => t.Date).HasColumnType("date");
        builder.HasIndex(t => t.PlaidTransactionId).IsUnique();
        builder.HasIndex(t => t.ConnectionId);
    }
}

public class BankCheckoutConfiguration : IEntityTypeConfiguration<BankCheckout>
{
    public void Configure(EntityTypeBuilder<BankCheckout> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.AccountId).HasMaxLength(100);
        builder.Property(c => c.LegalName).HasMaxLength(256);
        builder.Property(c => c.Status).IsRequired().HasMaxLength(20);
        builder.Property(c => c.Speed).HasMaxLength(20);
        builder.Property(c => c.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(c => c.ShippingCost).HasColumnType("decimal(18,2)");
        builder.Property(c => c.Fee).HasColumnType("decimal(18,2)");
        builder.Property(c => c.ArrivesOn).HasColumnType("date");
    }
}

public class BankPaymentConfiguration : IEntityTypeConfiguration<BankPayment>
{
    public void Configure(EntityTypeBuilder<BankPayment> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.TransferId).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Speed).IsRequired().HasMaxLength(20);
        builder.Property(p => p.Amount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Fee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.ArrivesOn).HasColumnType("date");
        builder.HasIndex(p => p.OrderId).IsUnique();
    }
}

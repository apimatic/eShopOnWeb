using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> builder)
    {
        builder.ToTable("BankConnections");
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.ItemId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.ProtectedAccessToken).IsRequired().HasMaxLength(2000);
        builder.Property(c => c.InstitutionId).HasMaxLength(100);
        builder.Property(c => c.InstitutionName).HasMaxLength(256);
        builder.Property(c => c.Kind).IsRequired().HasMaxLength(20);
        builder.Property(c => c.AccountId).HasMaxLength(100);
        builder.Property(c => c.AccountName).HasMaxLength(256);
        builder.Property(c => c.AccountMask).HasMaxLength(20);
        builder.Property(c => c.TransactionsCursor).HasMaxLength(2000);
        builder.HasIndex(c => c.BuyerId);
    }
}

public class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.ToTable("BankTransactions");
        builder.Property(t => t.PlaidTransactionId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.AccountId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(512);
        builder.Property(t => t.Amount).HasColumnType("decimal(18,2)");
        builder.Property(t => t.IsoCurrencyCode).HasMaxLength(10);
        builder.Property(t => t.Date).HasColumnType("date");
        builder.HasIndex(t => new { t.ConnectionId, t.PlaidTransactionId }).IsUnique();
    }
}

public class BankCheckoutConfiguration : IEntityTypeConfiguration<BankCheckout>
{
    public void Configure(EntityTypeBuilder<BankCheckout> builder)
    {
        builder.ToTable("BankCheckouts");
        builder.Ignore(c => c.Total);
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.ItemsJson).IsRequired();
        builder.Property(c => c.ShipToJson).IsRequired();
        builder.Property(c => c.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(c => c.ShippingCost).HasColumnType("decimal(18,2)");
        builder.Property(c => c.Fee).HasColumnType("decimal(18,2)");
        builder.Property(c => c.TotalCharged).HasColumnType("decimal(18,2)");
        builder.Property(c => c.Status).IsRequired().HasMaxLength(20);
        builder.Property(c => c.Speed).HasMaxLength(20);
        builder.Property(c => c.AuthorizationId).HasMaxLength(100);
        builder.Property(c => c.TransferId).HasMaxLength(100);
        builder.Property(c => c.RefundId).HasMaxLength(100);
        builder.Property(c => c.ArrivesOn).HasColumnType("date");
        builder.HasIndex(c => c.CheckoutId).IsUnique();
        builder.HasIndex(c => c.OrderId);
    }
}

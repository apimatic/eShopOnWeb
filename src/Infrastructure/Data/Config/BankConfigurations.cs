using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> builder)
    {
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.PlaidItemId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.AccessToken).IsRequired().HasMaxLength(200);
        builder.Property(c => c.AccountId).HasMaxLength(100);
        builder.Property(c => c.AccountName).HasMaxLength(200);
        builder.Property(c => c.AccountMask).HasMaxLength(20);
        builder.Property(c => c.InstitutionName).HasMaxLength(200);
        builder.Property(c => c.TransactionsCursor).HasMaxLength(500);
        builder.HasIndex(c => c.BuyerId);
    }
}

public class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.Property(t => t.PlaidTransactionId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.AccountId).HasMaxLength(100);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(500);
        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.Date).IsRequired().HasMaxLength(10);
        builder.HasIndex(t => new { t.ConnectionId, t.PlaidTransactionId }).IsUnique();
    }
}

public class BankCheckoutConfiguration : IEntityTypeConfiguration<BankCheckout>
{
    public void Configure(EntityTypeBuilder<BankCheckout> builder)
    {
        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.ShippingCost).HasPrecision(18, 2);
        builder.Property(c => c.Street).HasMaxLength(180);
        builder.Property(c => c.City).HasMaxLength(100);
        builder.Property(c => c.State).HasMaxLength(60);
        builder.Property(c => c.Country).HasMaxLength(90);
        builder.Property(c => c.ZipCode).HasMaxLength(18);
        builder.Property(c => c.ConcurrencyStamp).IsConcurrencyToken();
        builder.OwnsMany(c => c.Items, i =>
        {
            i.WithOwner().HasForeignKey("BankCheckoutId");
            i.HasKey(x => x.Id);
            i.Property(x => x.ProductName).IsRequired().HasMaxLength(50);
            i.Property(x => x.PictureUri).IsRequired();
            i.Property(x => x.UnitPrice).HasPrecision(18, 2);
        });
    }
}

public class BankPaymentConfiguration : IEntityTypeConfiguration<BankPayment>
{
    public void Configure(EntityTypeBuilder<BankPayment> builder)
    {
        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.OwnsMany(p => p.Transfers, t =>
        {
            t.WithOwner().HasForeignKey("BankPaymentId");
            t.HasKey(x => x.Id);
            t.Property(x => x.TransferId).IsRequired().HasMaxLength(100);
            t.Property(x => x.Amount).HasPrecision(18, 2);
        });
        builder.Property(p => p.Speed).IsRequired().HasMaxLength(20);
        builder.Property(p => p.Total).HasPrecision(18, 2);
        builder.Property(p => p.Fee).HasPrecision(18, 2);
        builder.Property(p => p.ArrivesOn).HasMaxLength(10);
        builder.Property(p => p.RefundId).HasMaxLength(100);
        builder.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
        builder.Ignore(p => p.Charged);
        builder.HasOne(p => p.Order).WithMany().HasForeignKey(p => p.OrderId);
        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.CheckoutId);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> builder)
    {
        builder.Property(c => c.UserName).IsRequired().HasMaxLength(256);
        builder.Property(c => c.UserKey).IsRequired().HasMaxLength(256);
        builder.Property(c => c.PlaidItemId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.ProtectedAccessToken).IsRequired();
        builder.Property(c => c.InstitutionId).HasMaxLength(100);
        builder.Property(c => c.InstitutionName).HasMaxLength(256);
        builder.Property(c => c.PaymentAccountId).HasMaxLength(100);
        builder.Property(c => c.PaymentAccountName).HasMaxLength(256);
        builder.Property(c => c.PaymentAccountMask).HasMaxLength(20);
        builder.HasIndex(c => c.UserKey);
        builder.HasIndex(c => c.PlaidItemId).IsUnique();
    }
}

public class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.Property(t => t.PlaidTransactionId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.AccountId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(512);
        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.Date).HasColumnType("date");
        builder.HasIndex(t => new { t.BankConnectionId, t.PlaidTransactionId }).IsUnique();
    }
}

public class BankCheckoutConfiguration : IEntityTypeConfiguration<BankCheckout>
{
    public void Configure(EntityTypeBuilder<BankCheckout> builder)
    {
        builder.Property(c => c.UserName).IsRequired().HasMaxLength(256);
        builder.Property(c => c.UserKey).IsRequired().HasMaxLength(256);
        builder.Property(c => c.ShippingCost).HasPrecision(18, 2);
        builder.Property(c => c.ShipStreet).IsRequired().HasMaxLength(180);
        builder.Property(c => c.ShipCity).IsRequired().HasMaxLength(100);
        builder.Property(c => c.ShipState).HasMaxLength(60);
        builder.Property(c => c.ShipCountry).IsRequired().HasMaxLength(90);
        builder.Property(c => c.ShipZipCode).IsRequired().HasMaxLength(18);
        builder.HasIndex(c => c.CheckoutId).IsUnique();
        builder.HasMany(c => c.Items).WithOne().HasForeignKey(i => i.BankCheckoutId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BankCheckoutItemConfiguration : IEntityTypeConfiguration<BankCheckoutItem>
{
    public void Configure(EntityTypeBuilder<BankCheckoutItem> builder)
    {
        builder.Property(i => i.ProductName).IsRequired().HasMaxLength(50);
        builder.Property(i => i.PictureUri).IsRequired();
        builder.Property(i => i.UnitPrice).HasPrecision(18, 2);
    }
}

public class BankPaymentConfiguration : IEntityTypeConfiguration<BankPayment>
{
    public void Configure(EntityTypeBuilder<BankPayment> builder)
    {
        builder.Property(p => p.UserName).IsRequired().HasMaxLength(256);
        builder.Property(p => p.UserKey).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Speed).IsRequired().HasMaxLength(20);
        builder.Property(p => p.OrderTotal).HasPrecision(18, 2);
        builder.Property(p => p.Fee).HasPrecision(18, 2);
        builder.Property(p => p.TotalCharged).HasPrecision(18, 2);
        builder.Property(p => p.ArrivesOn).HasColumnType("date");
        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.CheckoutId).IsUnique();
        builder.HasOne(p => p.Order).WithOne().HasForeignKey<BankPayment>(p => p.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Transfers).WithOne().HasForeignKey(t => t.BankPaymentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BankPaymentTransferConfiguration : IEntityTypeConfiguration<BankPaymentTransfer>
{
    public void Configure(EntityTypeBuilder<BankPaymentTransfer> builder)
    {
        builder.Property(t => t.PlaidTransferId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Network).IsRequired().HasMaxLength(30);
        builder.Property(t => t.Amount).HasPrecision(18, 2);
    }
}

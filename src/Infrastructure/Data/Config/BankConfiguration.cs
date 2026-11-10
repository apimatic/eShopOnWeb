using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> b)
    {
        b.HasKey(x => x.Id);
        b.Ignore(x => x.IsActive);
        b.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        b.Property(x => x.Kind).IsRequired().HasMaxLength(20);
        b.Property(x => x.ItemId).IsRequired().HasMaxLength(100);
        b.Property(x => x.AccessToken).HasMaxLength(200);
        b.Property(x => x.InstitutionId).HasMaxLength(50);
        b.Property(x => x.InstitutionName).HasMaxLength(200);
        b.Property(x => x.AccountId).HasMaxLength(100);
        b.Property(x => x.AccountName).HasMaxLength(200);
        b.Property(x => x.AccountMask).HasMaxLength(20);
        b.Property(x => x.SyncCursor).HasMaxLength(500);
        b.HasIndex(x => x.BuyerId);
        b.HasMany(x => x.Transactions).WithOne().HasForeignKey(t => t.ConnectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> b)
    {
        b.Property(x => x.PlaidTransactionId).IsRequired().HasMaxLength(100);
        b.Property(x => x.AccountId).IsRequired().HasMaxLength(100);
        b.Property(x => x.Name).IsRequired().HasMaxLength(500);
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        b.Property(x => x.IsoCurrencyCode).HasMaxLength(10);
        b.Property(x => x.PendingTransactionId).HasMaxLength(100);
        b.HasIndex(x => new { x.ConnectionId, x.PlaidTransactionId }).IsUnique();
    }
}

public class BankCheckoutConfiguration : IEntityTypeConfiguration<BankCheckout>
{
    public void Configure(EntityTypeBuilder<BankCheckout> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        b.Property(x => x.Status).IsRequired().HasMaxLength(20);
        b.Property(x => x.Shipping).HasColumnType("decimal(18,2)");
        b.Property(x => x.ShipStreet).HasMaxLength(180);
        b.Property(x => x.ShipCity).HasMaxLength(100);
        b.Property(x => x.ShipState).HasMaxLength(60);
        b.Property(x => x.ShipCountry).HasMaxLength(90);
        b.Property(x => x.ShipZip).HasMaxLength(18);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.CheckoutId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BankCheckoutItemConfiguration : IEntityTypeConfiguration<BankCheckoutItem>
{
    public void Configure(EntityTypeBuilder<BankCheckoutItem> b)
    {
        b.Property(x => x.Name).IsRequired().HasMaxLength(50);
        b.Property(x => x.PictureUri).IsRequired();
        b.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
    }
}

public class BankPaymentConfiguration : IEntityTypeConfiguration<BankPayment>
{
    public void Configure(EntityTypeBuilder<BankPayment> b)
    {
        b.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        b.Property(x => x.TransferId).IsRequired().HasMaxLength(100);
        b.Property(x => x.Speed).IsRequired().HasMaxLength(20);
        b.Property(x => x.Status).IsRequired().HasMaxLength(20);
        b.Property(x => x.RefundId).HasMaxLength(100);
        b.Property(x => x.OrderTotal).HasColumnType("decimal(18,2)");
        b.Property(x => x.Fee).HasColumnType("decimal(18,2)");
        b.Property(x => x.Charged).HasColumnType("decimal(18,2)");
        b.HasIndex(x => x.OrderId).IsUnique();
        b.HasIndex(x => x.CheckoutId).IsUnique();
    }
}

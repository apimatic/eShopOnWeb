using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> builder)
    {
        builder.Property(c => c.UserId).IsRequired().HasMaxLength(450);
        builder.Property(c => c.Kind).IsRequired().HasMaxLength(20);
        builder.Property(c => c.ItemId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.AccessToken).IsRequired().HasMaxLength(200);
        builder.Property(c => c.InstitutionId).HasMaxLength(100);
        builder.Property(c => c.InstitutionName).HasMaxLength(200);
        builder.Property(c => c.AccountId).HasMaxLength(100);
        builder.Property(c => c.AccountName).HasMaxLength(200);
        builder.Property(c => c.AccountMask).HasMaxLength(20);
        builder.Property(c => c.TransactionsCursor).HasMaxLength(500);
        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => new { c.UserId, c.ItemId }).IsUnique();
        builder.HasMany(c => c.Transactions).WithOne().HasForeignKey(t => t.BankConnectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.Property(t => t.PlaidTransactionId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.AccountId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(500);
        builder.Property(t => t.Amount).HasColumnType("decimal(18,2)");
        builder.Property(t => t.IsoCurrencyCode).HasMaxLength(10);
        builder.HasIndex(t => new { t.BankConnectionId, t.PlaidTransactionId }).IsUnique();
    }
}

public class BankCheckoutConfiguration : IEntityTypeConfiguration<BankCheckout>
{
    public void Configure(EntityTypeBuilder<BankCheckout> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Ignore(c => c.Subtotal);
        builder.Ignore(c => c.Total);
        builder.Property(c => c.UserId).IsRequired().HasMaxLength(450);
        builder.Property(c => c.BuyerName).IsRequired().HasMaxLength(256);
        builder.Property(c => c.LegalName).HasMaxLength(256);
        builder.Property(c => c.EmailAddress).HasMaxLength(256);
        builder.Property(c => c.Status).IsRequired().HasMaxLength(20);
        builder.Property(c => c.ShippingCost).HasColumnType("decimal(18,2)");
        builder.Property(c => c.ShipStreet).HasMaxLength(180);
        builder.Property(c => c.ShipCity).HasMaxLength(100);
        builder.Property(c => c.ShipState).HasMaxLength(60);
        builder.Property(c => c.ShipCountry).HasMaxLength(90);
        builder.Property(c => c.ShipZipCode).HasMaxLength(18);
        builder.HasIndex(c => c.UserId);
        builder.HasMany(c => c.Items).WithOne().HasForeignKey(i => i.BankCheckoutId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BankCheckoutItemConfiguration : IEntityTypeConfiguration<BankCheckoutItem>
{
    public void Configure(EntityTypeBuilder<BankCheckoutItem> builder)
    {
        builder.Property(i => i.ProductName).IsRequired().HasMaxLength(50);
        builder.Property(i => i.PictureUri).IsRequired();
        builder.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
    }
}

public class BankOrderConfiguration : IEntityTypeConfiguration<BankOrder>
{
    public void Configure(EntityTypeBuilder<BankOrder> builder)
    {
        builder.HasKey(o => o.OrderId);
        builder.Property(o => o.OrderId).ValueGeneratedNever();
        builder.Property(o => o.UserId).IsRequired().HasMaxLength(450);
        builder.Property(o => o.Speed).IsRequired().HasMaxLength(20);
        builder.Property(o => o.Status).IsRequired().HasMaxLength(20);
        builder.Property(o => o.TransferId).IsRequired().HasMaxLength(100);
        builder.Property(o => o.AuthorizationId).IsRequired().HasMaxLength(100);
        builder.Property(o => o.RefundId).HasMaxLength(100);
        builder.Property(o => o.Total).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Fee).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Charged).HasColumnType("decimal(18,2)");
        builder.HasIndex(o => o.UserId);
        builder.HasIndex(o => o.CheckoutId).IsUnique();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> builder)
    {
        builder.Property(c => c.UserId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.PlaidItemId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.ProtectedAccessToken).IsRequired();
        builder.Property(c => c.AccountId).HasMaxLength(100);
        builder.Property(c => c.AccountName).HasMaxLength(200);
        builder.Property(c => c.AccountMask).HasMaxLength(20);
        builder.Property(c => c.InstitutionId).HasMaxLength(100);
        builder.Property(c => c.InstitutionName).HasMaxLength(200);
        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => c.PlaidItemId).IsUnique();
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
        builder.Property(t => t.Date).HasColumnType("date");
        builder.HasIndex(t => new { t.ConnectionId, t.PlaidTransactionId }).IsUnique();
    }
}

public class BankCheckoutConfiguration : IEntityTypeConfiguration<BankCheckout>
{
    public void Configure(EntityTypeBuilder<BankCheckout> builder)
    {
        builder.Ignore(c => c.Total);
        builder.Property(c => c.UserId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(c => c.ShippingCost).HasColumnType("decimal(18,2)");
        builder.HasMany(c => c.Items).WithOne().HasForeignKey(i => i.CheckoutId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => c.ConnectionId);
    }
}

public class BankCheckoutItemConfiguration : IEntityTypeConfiguration<BankCheckoutItem>
{
    public void Configure(EntityTypeBuilder<BankCheckoutItem> builder)
    {
        builder.Property(i => i.ProductName).IsRequired().HasMaxLength(200);
        builder.Property(i => i.PictureUri).IsRequired();
        builder.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
    }
}

public class BankPaymentConfiguration : IEntityTypeConfiguration<BankPayment>
{
    public void Configure(EntityTypeBuilder<BankPayment> builder)
    {
        builder.Property(p => p.UserId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Speed).IsRequired().HasMaxLength(20);
        builder.Property(p => p.Fee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Charged).HasColumnType("decimal(18,2)");
        builder.Property(p => p.ArrivesOn).HasColumnType("date");
        builder.HasMany(p => p.Transfers).WithOne().HasForeignKey(t => t.BankPaymentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.CheckoutId).IsUnique();
    }
}

public class BankPaymentTransferConfiguration : IEntityTypeConfiguration<BankPaymentTransfer>
{
    public void Configure(EntityTypeBuilder<BankPaymentTransfer> builder)
    {
        builder.Property(t => t.PlaidTransferId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Network).IsRequired().HasMaxLength(30);
        builder.Property(t => t.Amount).HasColumnType("decimal(18,2)");
    }
}

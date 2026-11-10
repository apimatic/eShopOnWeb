using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> builder)
    {
        builder.ToTable("BankConnections");
        builder.Property(b => b.PublicId).IsRequired().HasMaxLength(40);
        builder.HasIndex(b => b.PublicId).IsUnique();
        builder.Property(b => b.OwnerKey).IsRequired().HasMaxLength(256);
        builder.HasIndex(b => b.OwnerKey);
        builder.Property(b => b.Kind).IsRequired().HasMaxLength(20);
        builder.Property(b => b.ItemId).IsRequired().HasMaxLength(100);
        builder.Property(b => b.ProtectedAccessToken).IsRequired();
        builder.Property(b => b.InstitutionId).HasMaxLength(50);
        builder.Property(b => b.InstitutionName).HasMaxLength(200);
        builder.HasMany(b => b.Accounts).WithOne().HasForeignKey(a => a.BankConnectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BankConnectionAccountConfiguration : IEntityTypeConfiguration<BankConnectionAccount>
{
    public void Configure(EntityTypeBuilder<BankConnectionAccount> builder)
    {
        builder.ToTable("BankConnectionAccounts");
        builder.Property(a => a.AccountId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Name).HasMaxLength(200);
        builder.Property(a => a.Mask).HasMaxLength(20);
        builder.Property(a => a.Type).HasMaxLength(50);
        builder.Property(a => a.Subtype).HasMaxLength(50);
    }
}

public class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.ToTable("BankTransactions");
        builder.Property(t => t.PlaidTransactionId).IsRequired().HasMaxLength(100);
        builder.HasIndex(t => new { t.BankConnectionId, t.PlaidTransactionId }).IsUnique();
        builder.Property(t => t.AccountId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(500);
        builder.Property(t => t.Amount).HasColumnType("decimal(18,2)");
        builder.Property(t => t.IsoCurrencyCode).HasMaxLength(10);
        builder.Property(t => t.PendingTransactionId).HasMaxLength(100);
        builder.HasOne<BankConnection>().WithMany().HasForeignKey(t => t.BankConnectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BankCheckoutConfiguration : IEntityTypeConfiguration<BankCheckout>
{
    public void Configure(EntityTypeBuilder<BankCheckout> builder)
    {
        builder.ToTable("BankCheckouts");
        builder.Ignore(c => c.Subtotal);
        builder.Ignore(c => c.Total);
        builder.Property(c => c.PublicId).IsRequired().HasMaxLength(40);
        builder.HasIndex(c => c.PublicId).IsUnique();
        builder.Property(c => c.OwnerKey).IsRequired().HasMaxLength(256);
        builder.Property(c => c.BuyerName).IsRequired().HasMaxLength(256);
        builder.Property(c => c.LegalName).IsRequired().HasMaxLength(256);
        builder.Property(c => c.PlaidAccountId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.ShippingCost).HasColumnType("decimal(18,2)");
        builder.Property(c => c.Status).IsRequired().HasMaxLength(20);
        builder.Property(c => c.AuthorizationSpeed).HasMaxLength(20);
        builder.HasMany(c => c.Authorizations).WithOne().HasForeignKey(a => a.BankCheckoutId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.Items).WithOne().HasForeignKey(i => i.BankCheckoutId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BankCheckoutItemConfiguration : IEntityTypeConfiguration<BankCheckoutItem>
{
    public void Configure(EntityTypeBuilder<BankCheckoutItem> builder)
    {
        builder.ToTable("BankCheckoutItems");
        builder.Property(i => i.Name).IsRequired().HasMaxLength(50);
        builder.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
    }
}

public class BankOrderConfiguration : IEntityTypeConfiguration<BankOrder>
{
    public void Configure(EntityTypeBuilder<BankOrder> builder)
    {
        builder.ToTable("BankOrders");
        builder.Property(o => o.OwnerKey).IsRequired().HasMaxLength(256);
        builder.HasIndex(o => o.OwnerKey);
        builder.Property(o => o.CheckoutPublicId).IsRequired().HasMaxLength(40);
        builder.HasIndex(o => o.CheckoutPublicId).IsUnique();
        builder.Property(o => o.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(o => o.ShippingCost).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Fee).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Total).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Speed).IsRequired().HasMaxLength(20);
        builder.Property(o => o.Status).IsRequired().HasMaxLength(20);
        builder.HasMany(o => o.Transfers).WithOne().HasForeignKey(t => t.BankOrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BankCheckoutAuthorizationConfiguration : IEntityTypeConfiguration<BankCheckoutAuthorization>
{
    public void Configure(EntityTypeBuilder<BankCheckoutAuthorization> builder)
    {
        builder.ToTable("BankCheckoutAuthorizations");
        builder.Property(a => a.Amount).HasColumnType("decimal(18,2)");
        builder.Property(a => a.AuthorizationId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.TransferId).HasMaxLength(100);
        builder.Property(a => a.ExpectedArrival).HasMaxLength(20);
    }
}

public class BankOrderTransferConfiguration : IEntityTypeConfiguration<BankOrderTransfer>
{
    public void Configure(EntityTypeBuilder<BankOrderTransfer> builder)
    {
        builder.ToTable("BankOrderTransfers");
        builder.Property(t => t.TransferId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Amount).HasColumnType("decimal(18,2)");
        builder.Property(t => t.RefundId).HasMaxLength(100);
    }
}

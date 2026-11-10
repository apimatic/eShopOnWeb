using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> builder)
    {
        builder.Property(c => c.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.ItemId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.ProtectedAccessToken).HasMaxLength(2000);
        builder.Property(c => c.InstitutionId).HasMaxLength(100);
        builder.Property(c => c.InstitutionName).HasMaxLength(200);
        builder.Property(c => c.AccountId).HasMaxLength(100);
        builder.Property(c => c.AccountName).HasMaxLength(200);
        builder.Property(c => c.AccountMask).HasMaxLength(20);
        builder.Property(c => c.SyncCursor).HasMaxLength(1000);
        builder.HasIndex(c => c.OwnerId);
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
        builder.HasIndex(t => new { t.ConnectionId, t.PlaidTransactionId }).IsUnique();
    }
}

public class BankCheckoutConfiguration : IEntityTypeConfiguration<BankCheckout>
{
    public void Configure(EntityTypeBuilder<BankCheckout> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasMaxLength(50);
        builder.Property(c => c.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(c => c.Shipping).HasColumnType("decimal(18,2)");
        builder.Ignore(c => c.Total);
    }
}

public class BankOrderConfiguration : IEntityTypeConfiguration<BankOrder>
{
    public void Configure(EntityTypeBuilder<BankOrder> builder)
    {
        builder.Property(o => o.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(o => o.CheckoutId).IsRequired().HasMaxLength(50);
        builder.Property(o => o.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Shipping).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Fee).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Charged).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Speed).HasMaxLength(20);
        builder.Property(o => o.TransferId).IsRequired().HasMaxLength(100);
        builder.Property(o => o.Status).IsRequired().HasMaxLength(20);
        builder.Property(o => o.RefundId).HasMaxLength(100);
        builder.HasIndex(o => o.OwnerId);
        builder.HasIndex(o => o.CheckoutId);
    }
}

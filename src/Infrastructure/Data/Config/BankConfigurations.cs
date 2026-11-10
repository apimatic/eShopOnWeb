using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> builder)
    {
        builder.ToTable("BankConnections");
        builder.HasIndex(c => c.PublicId).IsUnique();
        builder.HasIndex(c => c.UserName);
        builder.HasIndex(c => new { c.UserName, c.ItemId }).IsUnique();
        builder.Property(c => c.UserName).IsRequired().HasMaxLength(256);
        builder.Property(c => c.ItemId).IsRequired().HasMaxLength(128);
        builder.Property(c => c.ProtectedAccessToken).IsRequired();
        builder.Property(c => c.InstitutionId).HasMaxLength(64);
        builder.Property(c => c.InstitutionName).HasMaxLength(256);
        builder.Property(c => c.PaymentAccountId).HasMaxLength(128);
        builder.Property(c => c.PaymentAccountName).HasMaxLength(256);
        builder.Property(c => c.PaymentAccountMask).HasMaxLength(16);
        builder.HasMany(c => c.Transactions).WithOne().HasForeignKey(t => t.BankConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.ToTable("BankTransactions");
        builder.HasIndex(t => new { t.BankConnectionId, t.PlaidTransactionId }).IsUnique();
        builder.Property(t => t.PlaidTransactionId).IsRequired().HasMaxLength(128);
        builder.Property(t => t.AccountId).IsRequired().HasMaxLength(128);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(512);
        builder.Property(t => t.IsoCurrencyCode).HasMaxLength(8);
        builder.Property(t => t.Amount).HasColumnType("decimal(18,2)");
        builder.Property(t => t.Date).HasColumnType("date");
    }
}

public class BankCheckoutConfiguration : IEntityTypeConfiguration<BankCheckout>
{
    public void Configure(EntityTypeBuilder<BankCheckout> builder)
    {
        builder.ToTable("BankCheckouts");
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => c.UserName);
        builder.Property(c => c.UserName).IsRequired().HasMaxLength(256);
        builder.Property(c => c.ItemsTotal).HasColumnType("decimal(18,2)");
        builder.Property(c => c.ShippingCost).HasColumnType("decimal(18,2)");
        builder.Property(c => c.Version).IsConcurrencyToken();
    }
}

public class BankPaymentConfiguration : IEntityTypeConfiguration<BankPayment>
{
    public void Configure(EntityTypeBuilder<BankPayment> builder)
    {
        builder.ToTable("BankPayments");
        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.CheckoutId).IsUnique();
        builder.Property(p => p.UserName).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Speed).IsRequired().HasMaxLength(16);
        builder.Property(p => p.Total).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Fee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.ArrivesOn).HasColumnType("date");
        builder.Property(p => p.Version).IsConcurrencyToken();
        builder.HasMany(p => p.Transfers).WithOne().HasForeignKey(t => t.BankPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class BankPaymentTransferConfiguration : IEntityTypeConfiguration<BankPaymentTransfer>
{
    public void Configure(EntityTypeBuilder<BankPaymentTransfer> builder)
    {
        builder.ToTable("BankPaymentTransfers");
        builder.Property(t => t.PlaidTransferId).IsRequired().HasMaxLength(64);
        builder.Property(t => t.Amount).HasColumnType("decimal(18,2)");
    }
}

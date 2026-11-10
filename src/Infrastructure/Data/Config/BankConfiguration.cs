using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> builder)
    {
        builder.Property(c => c.UserName).IsRequired().HasMaxLength(256);
        builder.Property(c => c.Kind).IsRequired().HasMaxLength(20);
        builder.Property(c => c.ItemId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.ProtectedAccessToken).IsRequired();
        builder.Property(c => c.InstitutionId).HasMaxLength(100);
        builder.Property(c => c.InstitutionName).HasMaxLength(200);
        builder.Property(c => c.AccountId).HasMaxLength(100);
        builder.Property(c => c.AccountName).HasMaxLength(200);
        builder.Property(c => c.AccountMask).HasMaxLength(20);
        builder.HasIndex(c => c.UserName);
        builder.HasIndex(c => c.ItemId).IsUnique();
    }
}

public class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.Property(t => t.PlaidTransactionId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.AccountId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(500);
        builder.Property(t => t.PendingTransactionId).HasMaxLength(100);
        builder.Property(t => t.Amount).HasColumnType("decimal(18,2)");
        builder.Property(t => t.Date).HasColumnType("date");
        builder.HasIndex(t => new { t.ConnectionId, t.PlaidTransactionId }).IsUnique();
    }
}

public class BankCheckoutConfiguration : IEntityTypeConfiguration<BankCheckout>
{
    public void Configure(EntityTypeBuilder<BankCheckout> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.UserName).IsRequired().HasMaxLength(256);
        builder.Property(c => c.ItemsJson).IsRequired();
        builder.Property(c => c.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(c => c.ShippingCost).HasColumnType("decimal(18,2)");
        builder.Property(c => c.ConfirmedSpeed).HasMaxLength(20);
        builder.Property(c => c.Street).HasMaxLength(180);
        builder.Property(c => c.City).HasMaxLength(100);
        builder.Property(c => c.State).HasMaxLength(60);
        builder.Property(c => c.Country).HasMaxLength(90);
        builder.Property(c => c.ZipCode).HasMaxLength(18);
        builder.Ignore(c => c.Total);
        builder.Ignore(c => c.IsConfirmed);
    }
}

public class BankPaymentConfiguration : IEntityTypeConfiguration<BankPayment>
{
    public void Configure(EntityTypeBuilder<BankPayment> builder)
    {
        builder.Property(p => p.UserName).IsRequired().HasMaxLength(256);
        builder.Property(p => p.TransferId).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Speed).IsRequired().HasMaxLength(20);
        builder.Property(p => p.Total).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Fee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.ArrivesOn).HasColumnType("date");
        builder.Ignore(p => p.Charged);
        builder.HasIndex(p => p.OrderId).IsUnique();
    }
}

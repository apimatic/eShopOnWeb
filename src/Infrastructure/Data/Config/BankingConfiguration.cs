using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Banking;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> builder)
    {
        builder.Property(b => b.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(b => b.PlaidItemId).IsRequired().HasMaxLength(128);
        builder.Property(b => b.ProtectedAccessToken).IsRequired();
        builder.Property(b => b.InstitutionId).HasMaxLength(64);
        builder.Property(b => b.InstitutionName).HasMaxLength(256);
        builder.Property(b => b.AccountId).HasMaxLength(128);
        builder.Property(b => b.AccountName).HasMaxLength(256);
        builder.Property(b => b.AccountMask).HasMaxLength(16);
        builder.HasIndex(b => new { b.BuyerId, b.PlaidItemId }).IsUnique();
    }
}

public class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.Property(b => b.PlaidTransactionId).IsRequired().HasMaxLength(128);
        builder.Property(b => b.AccountId).IsRequired().HasMaxLength(128);
        builder.Property(b => b.Name).IsRequired().HasMaxLength(512);
        builder.Property(b => b.CurrencyCode).HasMaxLength(8);
        builder.Property(b => b.Amount).HasColumnType("decimal(18,2)");
        builder.HasIndex(b => new { b.ConnectionId, b.PlaidTransactionId }).IsUnique();
    }
}

public class BankCheckoutConfiguration : IEntityTypeConfiguration<BankCheckout>
{
    public void Configure(EntityTypeBuilder<BankCheckout> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(b => b.ItemsJson).IsRequired();
        builder.Property(b => b.ShipStreet).HasMaxLength(180);
        builder.Property(b => b.ShipCity).HasMaxLength(100);
        builder.Property(b => b.ShipState).HasMaxLength(60);
        builder.Property(b => b.ShipCountry).HasMaxLength(90);
        builder.Property(b => b.ShipZipCode).HasMaxLength(18);
        builder.Property(b => b.Speed).HasMaxLength(16);
        builder.Property(b => b.ShippingCost).HasColumnType("decimal(18,2)");
        builder.Property(b => b.Fee).HasColumnType("decimal(18,2)");
        builder.Property(b => b.ArrivesOn).HasColumnType("date");
        builder.Property(b => b.Revision).IsConcurrencyToken();
    }
}

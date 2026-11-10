using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> builder)
    {
        builder.HasIndex(c => c.PublicId).IsUnique();
        builder.HasIndex(c => c.PlaidItemId).IsUnique();
        builder.HasIndex(c => c.ShopperId);
        builder.Property(c => c.ShopperId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.PlaidItemId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.AccessTokenProtected).IsRequired();
        builder.Property(c => c.InstitutionId).HasMaxLength(100);
        builder.Property(c => c.InstitutionName).HasMaxLength(200);
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
    }
}

public class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.HasIndex(t => t.PlaidTransactionId).IsUnique();
        builder.HasIndex(t => t.ConnectionId);
        builder.Property(t => t.PlaidTransactionId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.AccountId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(500);
        builder.Property(t => t.Amount).HasPrecision(18, 4);
        builder.Property(t => t.IsoCurrencyCode).HasMaxLength(10);
        builder.Property(t => t.PendingTransactionId).HasMaxLength(100);
        builder.Property(t => t.Date).HasColumnType("date");
    }
}

public class BankCheckoutConfiguration : IEntityTypeConfiguration<BankCheckout>
{
    public void Configure(EntityTypeBuilder<BankCheckout> builder)
    {
        builder.HasIndex(c => c.PublicId).IsUnique();
        builder.HasIndex(c => c.OrderId);
        builder.Property(c => c.ShopperId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.AccountId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Currency).HasMaxLength(10);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Subtotal).HasPrecision(18, 2);
        builder.Property(c => c.ShippingCost).HasPrecision(18, 2);
        builder.Property(c => c.Fee).HasPrecision(18, 2);
        builder.Property(c => c.ChargedAmount).HasPrecision(18, 2);
        builder.Property(c => c.RefundedAmount).HasPrecision(18, 2);
        builder.Property(c => c.ArrivesOn).HasColumnType("date");
        builder.HasOne(c => c.Order).WithMany().HasForeignKey(c => c.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> builder)
    {
        builder.Property(c => c.UserName).IsRequired().HasMaxLength(256);
        builder.Property(c => c.ItemId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.AccessToken).IsRequired().HasMaxLength(200);
        builder.Property(c => c.InstitutionId).HasMaxLength(50);
        builder.Property(c => c.InstitutionName).HasMaxLength(200);
        builder.Property(c => c.PaymentAccountId).HasMaxLength(100);
        builder.Property(c => c.TransactionsCursor).HasMaxLength(500);
        builder.HasIndex(c => c.UserName);
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

public class BankPaymentConfiguration : IEntityTypeConfiguration<BankPayment>
{
    public void Configure(EntityTypeBuilder<BankPayment> builder)
    {
        builder.Property(p => p.UserName).IsRequired().HasMaxLength(256);
        builder.Property(p => p.PlaidAccountId).IsRequired().HasMaxLength(100);
        builder.Property(p => p.ItemsJson).IsRequired();
        builder.Property(p => p.ItemsTotal).HasColumnType("decimal(18,2)");
        builder.Property(p => p.ShippingCost).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Fee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.ChargedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Street).HasMaxLength(180);
        builder.Property(p => p.City).HasMaxLength(100);
        builder.Property(p => p.State).HasMaxLength(60);
        builder.Property(p => p.Country).HasMaxLength(90);
        builder.Property(p => p.ZipCode).HasMaxLength(18);
        builder.Property(p => p.Speed).HasMaxLength(20);
        builder.Property(p => p.AuthorizationId).HasMaxLength(100);
        builder.Property(p => p.TransferId).HasMaxLength(100);
        builder.Property(p => p.RefundId).HasMaxLength(100);
        builder.Property(p => p.ArrivesOn).HasColumnType("date");
        builder.HasIndex(p => p.CheckoutId).IsUnique();
        builder.HasIndex(p => p.OrderId);
        builder.HasOne(p => p.Order).WithMany().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

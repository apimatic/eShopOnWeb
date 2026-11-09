using System;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class ShopperAccount
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string StaxCustomerId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Registry of every payment method saved through the shop (so removed ones stay discoverable for support).</summary>
public class SavedPaymentMethod
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string StaxCustomerId { get; set; } = "";
    public string StaxPaymentMethodId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum PaymentState { Unpaid = 0, Processing = 1, PendingSettlement = 2, Paid = 3, Failed = 4 }

public enum RefundState { None = 0, Pending = 1, Refunded = 2, Failed = 3, NotRequired = 4 }

public class OrderPayment
{
    public int OrderId { get; set; }
    public string BuyerEmail { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? Method { get; set; }
    public PaymentState State { get; set; }
    public string? AttemptKey { get; set; }
    public string? StaxTransactionId { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? LastError { get; set; }
    public RefundState RefundState { get; set; }
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? RefundMessage { get; set; }
}

public class InvoiceScheduleRecord
{
    public int Id { get; set; }
    public string StaxScheduleId { get; set; } = "";
    public string StaxCustomerId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Company { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime FirstInvoiceDate { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SellerRecord
{
    public int Id { get; set; }
    public string StaxMerchantId { get; set; } = "";
    public string Company { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class PaymentsDbContext : DbContext
{
    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : base(options) { }

    public DbSet<ShopperAccount> ShopperAccounts => Set<ShopperAccount>();
    public DbSet<SavedPaymentMethod> SavedPaymentMethods => Set<SavedPaymentMethod>();
    public DbSet<OrderPayment> OrderPayments => Set<OrderPayment>();
    public DbSet<InvoiceScheduleRecord> InvoiceSchedules => Set<InvoiceScheduleRecord>();
    public DbSet<SellerRecord> Sellers => Set<SellerRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ShopperAccount>(e =>
        {
            e.ToTable("StaxShopperAccounts");
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.StaxCustomerId).HasMaxLength(64);
        });
        b.Entity<SavedPaymentMethod>(e =>
        {
            e.ToTable("StaxSavedPaymentMethods");
            e.HasIndex(x => x.StaxPaymentMethodId).IsUnique();
            e.HasIndex(x => x.Email);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.StaxPaymentMethodId).HasMaxLength(64);
            e.Property(x => x.StaxCustomerId).HasMaxLength(64);
        });
        b.Entity<OrderPayment>(e =>
        {
            e.ToTable("StaxOrderPayments");
            e.HasKey(x => x.OrderId);
            e.Property(x => x.OrderId).ValueGeneratedNever();
            e.HasIndex(x => x.BuyerEmail);
            e.Property(x => x.BuyerEmail).HasMaxLength(256);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Fee).HasPrecision(18, 2);
            e.Property(x => x.Total).HasPrecision(18, 2);
        });
        b.Entity<InvoiceScheduleRecord>(e =>
        {
            e.ToTable("StaxInvoiceSchedules");
            e.HasIndex(x => x.StaxScheduleId).IsUnique();
            e.Property(x => x.StaxScheduleId).HasMaxLength(64);
            e.Property(x => x.Amount).HasPrecision(18, 2);
        });
        b.Entity<SellerRecord>(e =>
        {
            e.ToTable("StaxSellers");
        });
    }
}

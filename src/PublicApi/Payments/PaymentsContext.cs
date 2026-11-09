using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class PaymentState
{
    public const string UNPAID = "unpaid";
    public const string PROCESSING = "processing";
    public const string PENDING = "pending";   // bank payment submitted, not yet settled
    public const string PAID = "paid";
    public const string FAILED = "failed";
}

public static class RefundState
{
    public const string NONE = "none";
    public const string PENDING = "pending";   // refund requested, Stax cannot carry it out yet; retried automatically
    public const string REFUNDED = "refunded";
    public const string FAILED = "failed";
}

public class ShopOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BuyerEmail { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public List<ShopOrderLine> Lines { get; set; } = new();

    public string PaymentStatus { get; set; } = PaymentState.UNPAID;
    public Guid? PaymentMethodId { get; set; }
    public string? PaymentMethodKind { get; set; }
    public Guid? StaxTransactionId { get; set; }
    public DateTime? PaymentSubmittedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? PaymentError { get; set; }

    public string RefundStatus { get; set; } = RefundState.NONE;
    public DateTime? RefundRequestedAt { get; set; }
    public string? RefundRequestedBy { get; set; }
    public decimal? RefundAmount { get; set; }
    public DateTime? RefundedAt { get; set; }
    public DateTime? RefundNextAttemptAt { get; set; }
    public int RefundAttempts { get; set; }
    public string? RefundNote { get; set; }

    /// <summary>True once the buyer closed their account: the row is kept only as an anonymous ledger entry.</summary>
    public bool Anonymized { get; set; }
}

public class ShopOrderLine
{
    public int Id { get; set; }
    public Guid ShopOrderId { get; set; }
    public int CatalogItemId { get; set; }
    public string ItemName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

/// <summary>A payment method the shop has seen for a shopper (kept so removed ones can still be listed for support).</summary>
public class PaymentMethodRecord
{
    public Guid Id { get; set; }    // the Stax payment method id
    public string BuyerEmail { get; set; } = "";
    public Guid StaxCustomerId { get; set; }
    public string Method { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PaymentsContext : DbContext
{
    public PaymentsContext(DbContextOptions<PaymentsContext> options) : base(options)
    {
    }

    public DbSet<ShopOrder> Orders => Set<ShopOrder>();
    public DbSet<ShopOrderLine> OrderLines => Set<ShopOrderLine>();
    public DbSet<PaymentMethodRecord> PaymentMethods => Set<PaymentMethodRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ShopOrder>(e =>
        {
            e.HasKey(o => o.Id);
            e.HasIndex(o => o.BuyerEmail);
            e.Property(o => o.BuyerEmail).HasMaxLength(256);
            e.Property(o => o.Subtotal).HasColumnType("decimal(18,2)");
            e.Property(o => o.Fee).HasColumnType("decimal(18,2)");
            e.Property(o => o.Total).HasColumnType("decimal(18,2)");
            e.Property(o => o.RefundAmount).HasColumnType("decimal(18,2)");
            e.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.ShopOrderId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ShopOrderLine>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.UnitPrice).HasColumnType("decimal(18,2)");
        });
        b.Entity<PaymentMethodRecord>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.BuyerEmail);
            e.Property(p => p.BuyerEmail).HasMaxLength(256);
        });
    }
}

using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class PaymentState
{
    public const string UNPAID = "Unpaid";
    public const string PROCESSING = "Processing";
    /// <summary>Bank payment accepted by Stax but not yet settled by the bank.</summary>
    public const string PENDING = "Pending";
    public const string PAID = "Paid";
    /// <summary>The bank returned the payment after it was accepted.</summary>
    public const string FAILED = "Failed";
}

public static class RefundState
{
    public const string NONE = "none";
    /// <summary>Requested by staff; Stax cannot carry it out yet, the shop keeps retrying.</summary>
    public const string PENDING = "pending";
    public const string REFUNDED = "refunded";
    public const string FAILED = "failed";
}

public class ShopOrder
{
    public int Id { get; set; }
    public string BuyerEmail { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public decimal Subtotal { get; set; }
    public List<ShopOrderLine> Lines { get; set; } = new();

    public string PaymentState { get; set; } = Payments.PaymentState.UNPAID;
    /// <summary>"card" or "bank".</summary>
    public string? PaymentMethodKind { get; set; }
    public string? PaymentMethodId { get; set; }
    public decimal Fee { get; set; }
    public decimal ChargedTotal { get; set; }
    public string? StaxTransactionId { get; set; }
    public int PayAttempts { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? FailureReason { get; set; }

    public string RefundStatus { get; set; } = RefundState.NONE;
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public DateTimeOffset? NextRefundAttemptAt { get; set; }
    public string? RefundNote { get; set; }

    /// <summary>Changes on every write so concurrent pay/refund attempts cannot both win.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();
}

public class ShopOrderLine
{
    public int Id { get; set; }
    public int ShopOrderId { get; set; }
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>Links a shop account to its customer record at Stax.</summary>
public class ShopperCustomer
{
    public string Email { get; set; } = "";
    public string StaxCustomerId { get; set; } = "";
}

public class PaymentsDbContext : DbContext
{
    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : base(options) { }

    public DbSet<ShopOrder> Orders => Set<ShopOrder>();
    public DbSet<ShopperCustomer> Shoppers => Set<ShopperCustomer>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ShopOrder>(e =>
        {
            e.ToTable("StaxShopOrders");
            e.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.ShopOrderId).OnDelete(DeleteBehavior.Cascade);
            e.Property(o => o.BuyerEmail).HasMaxLength(256).IsRequired();
            e.Property(o => o.Subtotal).HasPrecision(18, 2);
            e.Property(o => o.Fee).HasPrecision(18, 2);
            e.Property(o => o.ChargedTotal).HasPrecision(18, 2);
            e.Property(o => o.Version).IsConcurrencyToken();
            e.HasIndex(o => o.BuyerEmail);
        });
        b.Entity<ShopOrderLine>(e =>
        {
            e.ToTable("StaxShopOrderLines");
            e.Property(l => l.UnitPrice).HasPrecision(18, 2);
        });
        b.Entity<ShopperCustomer>(e =>
        {
            e.ToTable("StaxShopperCustomers");
            e.HasKey(s => s.Email);
            e.Property(s => s.Email).HasMaxLength(256);
        });
    }
}

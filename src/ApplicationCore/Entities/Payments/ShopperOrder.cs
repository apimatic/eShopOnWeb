using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

public enum PaymentState { None = 0, Processing = 1, Pending = 2, Paid = 3, Failed = 4 }

public enum RefundState { None = 0, Pending = 1, Refunded = 2, NotRequired = 3 }

/// <summary>An order placed through the public API, paid through Stax.</summary>
public class ShopperOrder : BaseEntity
{
    public string BuyerEmail { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ShopperOrderLine> Lines { get; set; } = new();

    public decimal Subtotal { get; set; }
    /// <summary>Card processing fee passed on to the shopper (zero until paid).</summary>
    public decimal Fee { get; set; }
    /// <summary>Amount charged: Subtotal + Fee (zero until paid).</summary>
    public decimal Total { get; set; }

    public PaymentState PaymentState { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? PaymentMethodType { get; set; }
    public string? StaxTransactionId { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? PaymentError { get; set; }
    /// <summary>Incremented on every payment attempt; used for Stax idempotency and as a concurrency token.</summary>
    public int PaymentAttempts { get; set; }
    /// <summary>Random per-order part of the Stax idempotency key (Stax keys are merchant-wide, so they must be globally unique).</summary>
    public string PaymentKey { get; set; } = Guid.NewGuid().ToString("N");

    public RefundState RefundState { get; set; }
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public string? RefundTransactionId { get; set; }
    public string? RefundError { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
}

public class ShopperOrderLine : BaseEntity
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>Maps a shop account (by email) to its customer record at Stax.</summary>
public class StaxCustomerLink : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string StaxCustomerId { get; set; } = string.Empty;
}

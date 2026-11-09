using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Stax;

public static class PayState
{
    public const string Unpaid = "unpaid";
    public const string Charging = "charging";
    public const string PendingSettlement = "pending_settlement";
    public const string Paid = "paid";
    public const string Failed = "failed";
}

public static class RefundState
{
    public const string None = "none";
    public const string Pending = "pending";
    public const string Refunded = "refunded";
    public const string Failed = "failed";
}

/// <summary>Maps a shopper (by email) to the Stax customer record(s) that hold their payment methods.</summary>
public class StaxCustomerLink
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string StaxCustomerId { get; set; } = "";
}

public class ShopOrder
{
    public int Id { get; set; }
    public string BuyerEmail { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal Subtotal { get; set; }
    public List<ShopOrderLine> Lines { get; set; } = new();

    public string PayState { get; set; } = Stax.PayState.Unpaid;
    public int PayAttempts { get; set; }
    /// <summary>Stax idempotency key of the charge in flight; kept across retries whose outcome is unknown.</summary>
    public string? PayIdempotencyKey { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? PaymentMethodType { get; set; }   // card | bank
    public decimal Fee { get; set; }
    public decimal Total { get; set; }                // amount actually charged (subtotal + fee)
    public string? StaxTransactionId { get; set; }
    public string? PaymentMessage { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    public string RefundStatus { get; set; } = RefundState.None;
    public DateTime? RefundRequestedAtUtc { get; set; }
    public DateTime? RefundCompletedAtUtc { get; set; }
    public decimal RefundedAmount { get; set; }
    public string? RefundTransactionId { get; set; }
    public string? RefundNote { get; set; }
    public DateTime? LastCheckedAtUtc { get; set; }
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

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

public static class PaymentStatuses
{
    public const string Processing = "processing"; // charge accepted, bank has not settled it yet
    public const string Paid = "paid";
    public const string Failed = "failed";
}

public static class RefundStatuses
{
    public const string None = "none";
    public const string Pending = "pending";     // requested, waiting for Stax to allow it (bank payments)
    public const string Refunded = "refunded";
    public const string NotRequired = "not_required"; // the payment itself failed after the refund was requested
}

/// <summary>
/// The Stax payment (and later refund) of one order.
/// </summary>
public class OrderPayment : BaseEntity
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string PaymentMethodId { get; set; } = string.Empty;
    public string MethodType { get; set; } = string.Empty; // card | bank
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal ChargedTotal { get; set; }
    public string StaxTransactionId { get; set; } = string.Empty;
    public string Status { get; set; } = PaymentStatuses.Processing;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }
    public string? LastError { get; set; }

    public string RefundStatus { get; set; } = RefundStatuses.None;
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? RefundTransactionId { get; set; }
    public string? RefundLastError { get; set; }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public static class PaymentStatuses
{
    public const string Processing = "processing";
    public const string Pending = "pending";   // bank payment accepted, waiting for the bank to settle it
    public const string Paid = "paid";
    public const string Failed = "failed";
}

public static class RefundStatuses
{
    public const string None = "none";
    public const string Pending = "pending";   // requested; will be carried out automatically (e.g. ACH waiting period)
    public const string Refunded = "refunded";
    public const string Failed = "failed";
}

/// <summary>Payment (and refund) state of an order, mirrored from the Stax transaction.</summary>
public class OrderPayment : BaseEntity
{
    public int OrderId { get; set; }
    public string BuyerEmail { get; set; } = string.Empty;
    public string StaxPaymentMethodId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty; // card | bank
    public string? StaxTransactionId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = PaymentStatuses.Processing;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }
    public string? LastError { get; set; }

    public string RefundStatus { get; set; } = RefundStatuses.None;
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public decimal RefundedAmount { get; set; }
    public string? RefundMessage { get; set; }
}

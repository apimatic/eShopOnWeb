using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

/// <summary>
/// Payment and refund state of one order, as last seen at Stax.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>none | processing | pending | paid | failed</summary>
    public string PaymentStatus { get; set; } = PaymentStatuses.None;
    public string? PaymentMethodId { get; set; }
    /// <summary>card | bank</summary>
    public string? Method { get; set; }
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public decimal Total { get; set; }
    public string? StaxTransactionId { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>none | pending | refunded</summary>
    public string RefundStatus { get; set; } = RefundStatuses.None;
    public string? RefundNote { get; set; }
    public DateTimeOffset? RefundRequestedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
}

public static class PaymentStatuses
{
    public const string None = "none";
    public const string Processing = "processing";
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string Failed = "failed";
}

public static class RefundStatuses
{
    public const string None = "none";
    public const string Pending = "pending";
    public const string Refunded = "refunded";
}

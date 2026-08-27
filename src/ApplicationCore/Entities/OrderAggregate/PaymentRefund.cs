using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public static class PaymentRefundStatus
{
    public const string Completed = "COMPLETED";
    public const string Pending = "PENDING";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>
/// A single (full or partial) refund issued against a captured payment.
/// The caller-supplied idempotency key guarantees a repeated request never refunds twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() {}

    public PaymentRefund(int orderPaymentId, string refundId, string idempotencyKey, decimal amount, string currencyCode, string status)
    {
        OrderPaymentId = orderPaymentId;
        RefundId = refundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }

    /// <summary>PayPal's id for the refund resource.</summary>
    public string RefundId { get; private set; }

    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

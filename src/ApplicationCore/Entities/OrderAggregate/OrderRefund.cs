using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum RefundStatus
{
    /// <summary>Claimed and sent (or about to be sent) to the payment provider.</summary>
    Requested = 0,

    /// <summary>The provider accepted the refund request; the money is on its way back.</summary>
    Received = 1,

    /// <summary>The provider rejected the refund. No money was given back.</summary>
    Failed = 2,

    /// <summary>The call may or may not have reached the provider. Settled by re-sending with the same idempotency key.</summary>
    Unknown = 3
}

/// <summary>
/// Money given back on a paid order. Every refund that has not failed counts against what is still refundable,
/// so an order can never be refunded beyond what was paid.
/// </summary>
public class OrderRefund
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    internal OrderRefund(Guid id, int orderId, int paymentAttemptNumber, string idempotencyKey, long amountMinor,
        string currency, string? reason, string requestedBy, DateTimeOffset now)
    {
        Guard.Against.Default(id, nameof(id));
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amountMinor, nameof(amountMinor));
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));
        Guard.Against.NullOrWhiteSpace(requestedBy, nameof(requestedBy));

        Id = id;
        OrderId = orderId;
        PaymentAttemptNumber = paymentAttemptNumber;
        IdempotencyKey = idempotencyKey;
        MerchantReference = $"eshop-order-{orderId}-refund-{id.ToString("N")[..12]}";
        AmountMinor = amountMinor;
        Currency = currency;
        Reason = reason;
        RequestedBy = requestedBy;
        Status = RefundStatus.Requested;
        CreatedAt = now;
        LastSentAt = now;
    }

    public Guid Id { get; private set; }
    public int OrderId { get; private set; }

    /// <summary>The payment attempt whose money is given back.</summary>
    public int PaymentAttemptNumber { get; private set; }

    public string IdempotencyKey { get; private set; }
    public string MerchantReference { get; private set; }
    public long AmountMinor { get; private set; }
    public string Currency { get; private set; }
    public string? Reason { get; private set; }
    public string RequestedBy { get; private set; }

    public RefundStatus Status { get; private set; }
    public string? PspReference { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSentAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Counts against the refundable balance unless the provider definitely rejected it.</summary>
    public bool CountsAgainstBalance => Status != RefundStatus.Failed;

    internal void MarkResent(DateTimeOffset now)
    {
        Status = RefundStatus.Requested;
        LastSentAt = now;
    }

    internal void RecordOutcome(RefundStatus status, string? pspReference, string? failureMessage, DateTimeOffset now)
    {
        Status = status;
        PspReference = pspReference ?? PspReference;
        FailureMessage = failureMessage;
        CompletedAt = status is RefundStatus.Requested or RefundStatus.Unknown ? null : now;
    }
}

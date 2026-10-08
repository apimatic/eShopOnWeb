using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A refund an operator requested against an order's captured payment. The row doubles as the
/// duplicate-refund claim: its key is derived from the order and the refund sequence number.
/// </summary>
public class OrderRefund
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(int orderId, int sequence, OrderPayment payment, long amountMinor, decimal amount,
        string? reason, string requestedBy, string? clientRequestId, DateTimeOffset now)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.OutOfRange(sequence, nameof(sequence), 1, int.MaxValue);
        Guard.Against.Null(payment, nameof(payment));
        Guard.Against.NullOrWhiteSpace(payment.PspReference, nameof(payment.PspReference));
        Guard.Against.OutOfRange(amountMinor, nameof(amountMinor), 1, payment.AmountMinor);
        Guard.Against.NullOrWhiteSpace(requestedBy, nameof(requestedBy));

        Id = BuildId(orderId, sequence);
        OrderId = orderId;
        Sequence = sequence;
        PaymentId = payment.Id;
        PaymentPspReference = payment.PspReference;
        IdempotencyKey = Guid.NewGuid().ToString();
        MerchantReference = $"eshop-{Id}-{IdempotencyKey[..8]}";
        Currency = payment.Currency;
        AmountMinor = amountMinor;
        Amount = amount;
        Reason = reason;
        RequestedBy = requestedBy;
        ClientRequestId = clientRequestId;
        Status = RefundStatus.Processing;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static string BuildId(int orderId, int sequence) => $"{orderId}-R{sequence}";

    public string Id { get; private set; }
    public int OrderId { get; private set; }
    public int Sequence { get; private set; }
    public string PaymentId { get; private set; }
    public string PaymentPspReference { get; private set; }

    /// <summary>Sent to the provider as its idempotency key; reused for every re-send of this refund.</summary>
    public string IdempotencyKey { get; private set; }
    public string MerchantReference { get; private set; }
    public string Currency { get; private set; }
    public long AmountMinor { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>The provider's refund reason code, when the operator gave one.</summary>
    public string? Reason { get; private set; }
    public string RequestedBy { get; private set; }

    /// <summary>The operator's own Idempotency-Key for this refund request, when one was sent.</summary>
    public string? ClientRequestId { get; private set; }
    public RefundStatus Status { get; private set; }
    public string? PspReference { get; private set; }
    public string? Message { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The refund has given, or may have given, money back, so it counts against the refundable amount.</summary>
    public bool CountsAgainstPayment => Status is not RefundStatus.Rejected;

    public bool IsUnsettled => Status is RefundStatus.Processing or RefundStatus.Unknown;

    public void MarkReceived(string pspReference, DateTimeOffset now)
    {
        Status = RefundStatus.Received;
        PspReference = pspReference;
        Message = null;
        UpdatedAt = now;
    }

    public void MarkRejected(string message, DateTimeOffset now)
    {
        Status = RefundStatus.Rejected;
        Message = message;
        UpdatedAt = now;
    }

    public void MarkUnknown(string message, DateTimeOffset now)
    {
        Status = RefundStatus.Unknown;
        Message = message;
        UpdatedAt = now;
    }
}

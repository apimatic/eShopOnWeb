using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Money given back on a paid order. Its key (<see cref="OrderId"/>, <see cref="Sequence"/>) is the claim that
/// serialises refunds on one order, so the "never beyond what was paid" check cannot be raced.
/// </summary>
public class OrderRefund
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() {}

    internal OrderRefund(int orderId, int sequence, Guid refundId, string idempotencyKey, string? clientRequestKey,
        string reference, decimal amount, long amountInMinorUnits, string currency, string? reason,
        string paymentPspReference, DateTimeOffset createdAt)
    {
        Guard.Against.OutOfRange(sequence, nameof(sequence), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(reference, nameof(reference));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(paymentPspReference, nameof(paymentPspReference));
        Guard.Against.NegativeOrZero(amountInMinorUnits, nameof(amountInMinorUnits));

        OrderId = orderId;
        Sequence = sequence;
        RefundId = refundId;
        IdempotencyKey = idempotencyKey;
        ClientRequestKey = clientRequestKey;
        Reference = reference;
        Amount = amount;
        AmountInMinorUnits = amountInMinorUnits;
        Currency = currency;
        Reason = reason;
        PaymentPspReference = paymentPspReference;
        CreatedAt = createdAt;
        Status = RefundStatus.InFlight;
    }

    public int OrderId { get; private set; }
    public int Sequence { get; private set; }
    public Guid RefundId { get; private set; }

    /// <summary>Sent to the provider as its idempotency key; reused verbatim when an unknown outcome is settled.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>Optional key supplied by the operator so a retried request returns this refund instead of creating another.</summary>
    public string? ClientRequestKey { get; private set; }

    public string Reference { get; private set; }
    public decimal Amount { get; private set; }
    public long AmountInMinorUnits { get; private set; }
    public string Currency { get; private set; }
    public string? Reason { get; private set; }

    /// <summary>The provider reference of the authorised payment this refund gives money back on.</summary>
    public string PaymentPspReference { get; private set; }

    public RefundStatus Status { get; private set; }
    public string? PspReference { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>True while the amount counts against what can still be refunded.</summary>
    public bool ReservesAmount => Status is RefundStatus.InFlight or RefundStatus.Received or RefundStatus.Unknown;

    /// <summary>Unknown, or claimed so long ago that the request which claimed it cannot still be running.</summary>
    public bool NeedsSettlement(DateTimeOffset now, TimeSpan staleAfter) =>
        Status == RefundStatus.Unknown || (Status == RefundStatus.InFlight && now - CreatedAt >= staleAfter);

    internal void Complete(RefundStatus status, string? pspReference, string? errorCode, string? errorMessage, DateTimeOffset now)
    {
        Status = status;
        PspReference = pspReference ?? PspReference;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        CompletedAt = status == RefundStatus.Unknown ? null : now;
    }
}

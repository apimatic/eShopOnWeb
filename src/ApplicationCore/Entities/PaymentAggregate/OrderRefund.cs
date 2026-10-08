using System;
using System.Security.Cryptography;
using System.Text;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum RefundStatus
{
    /// <summary>Claimed; the amount is reserved on the order and the request is being sent.</summary>
    InFlight = 0,

    /// <summary>The processor accepted the refund request; the money is on its way back to the shopper.</summary>
    Received = 1,

    /// <summary>The processor rejected the refund. Terminal; the reserved amount was released.</summary>
    Failed = 2,

    /// <summary>The processor may or may not have acted. The amount stays reserved until a same-key resend settles it.</summary>
    Unknown = 3
}

/// <summary>
/// A refund on a paid order. The primary key is the refund id and the claim: a repeated request with the
/// same caller idempotency key maps to the same key and is refused by the store.
/// </summary>
public class OrderRefund : IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(string id, int orderId, decimal amount, long amountMinorUnits, string currency, string requestedBy, string? reason, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(id, nameof(id));
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NegativeOrZero(amountMinorUnits, nameof(amountMinorUnits));
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));
        Guard.Against.NullOrWhiteSpace(requestedBy, nameof(requestedBy));

        Id = id;
        OrderId = orderId;
        Amount = amount;
        AmountMinorUnits = amountMinorUnits;
        Currency = currency;
        RequestedBy = requestedBy;
        Reason = reason;
        IdempotencyKey = Guid.NewGuid().ToString("N");
        Status = RefundStatus.InFlight;
        CreatedDate = now;
        UpdatedDate = now;
    }

    /// <summary>
    /// Refund id for a request. With a caller-supplied idempotency key the id is deterministic, so the same
    /// key always maps to the same refund; without one every request is a new refund.
    /// </summary>
    public static string NewId(int orderId, string? callerIdempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(callerIdempotencyKey))
        {
            return $"rf_{Guid.NewGuid():N}";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{orderId}:{callerIdempotencyKey.Trim()}"));
        return $"rf_{orderId}_{Convert.ToHexString(hash, 0, 12).ToLowerInvariant()}";
    }

    public string Id { get; private set; }
    public int OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public long AmountMinorUnits { get; private set; }
    public string Currency { get; private set; }
    public string RequestedBy { get; private set; }
    public string? Reason { get; private set; }

    /// <summary>Sent to the processor as its idempotency key; reused verbatim on every resend of this refund.</summary>
    public string IdempotencyKey { get; private set; }
    public RefundStatus Status { get; private set; }
    public string? PspReference { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset UpdatedDate { get; private set; }

    /// <summary>True while the reserved amount counts against the order (anything but a failed refund).</summary>
    public bool HoldsReservation => Status != RefundStatus.Failed;

    public bool NeedsSettlement(DateTimeOffset now) =>
        Status == RefundStatus.Unknown
        || (Status == RefundStatus.InFlight && now - UpdatedDate > PaymentAttempt.InFlightLease);

    public void MarkResending(DateTimeOffset now)
    {
        Status = RefundStatus.InFlight;
        UpdatedDate = now;
    }

    public void MarkReceived(string pspReference, DateTimeOffset now)
    {
        PspReference = pspReference;
        Status = RefundStatus.Received;
        FailureReason = null;
        UpdatedDate = now;
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        FailureReason = reason;
        Status = RefundStatus.Failed;
        UpdatedDate = now;
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = RefundStatus.Unknown;
        UpdatedDate = now;
    }
}

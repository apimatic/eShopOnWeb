using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentAttemptStatus
{
    /// <summary>Claimed and sent (or about to be sent) to the processor.</summary>
    InFlight = 0,
    Authorised = 1,

    /// <summary>The processor declined the card or the payment could not be completed. Terminal; no money was taken.</summary>
    Refused = 2,

    /// <summary>The processor may or may not have acted. Settled by re-sending with the same idempotency key.</summary>
    Unknown = 3,

    /// <summary>An unusable authorisation (e.g. partial) was taken and a full reversal was requested. Terminal; the order is not paid.</summary>
    Reversed = 4,

    /// <summary>A reversal was needed but its outcome is unknown. Requires operator follow-up using <see cref="PaymentAttempt.PspReference"/>.</summary>
    ReversalUnknown = 5
}

/// <summary>
/// One attempt to charge an order. The primary key <c>pa_{orderId}_{n}</c> is the claim: two concurrent
/// pay requests compute the same key and the store refuses the second insert.
/// </summary>
public class PaymentAttempt : IAggregateRoot
{
    /// <summary>An in-flight attempt older than this is assumed to have lost its request and is settled like an unknown one.</summary>
    public static readonly TimeSpan InFlightLease = TimeSpan.FromMinutes(2);

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentAttempt() { }

    public PaymentAttempt(int orderId, int attemptNumber, long amountMinorUnits, decimal amount, string currency, DateTimeOffset now)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NegativeOrZero(attemptNumber, nameof(attemptNumber));
        Guard.Against.NegativeOrZero(amountMinorUnits, nameof(amountMinorUnits));
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));

        Id = KeyFor(orderId, attemptNumber);
        OrderId = orderId;
        AttemptNumber = attemptNumber;
        IdempotencyKey = Guid.NewGuid().ToString("N");
        AmountMinorUnits = amountMinorUnits;
        Amount = amount;
        Currency = currency;
        Status = PaymentAttemptStatus.InFlight;
        CreatedDate = now;
        UpdatedDate = now;
    }

    public static string KeyFor(int orderId, int attemptNumber) => $"pa_{orderId}_{attemptNumber}";

    public string Id { get; private set; }
    public int OrderId { get; private set; }
    public int AttemptNumber { get; private set; }

    /// <summary>Sent to the processor as its idempotency key; reused verbatim on every resend of this attempt.</summary>
    public string IdempotencyKey { get; private set; }
    public long AmountMinorUnits { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentAttemptStatus Status { get; private set; }
    public string? PspReference { get; private set; }
    public string? ResultCode { get; private set; }
    public string? RefusalReason { get; private set; }
    public string? RefusalReasonCode { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset UpdatedDate { get; private set; }

    /// <summary>Merchant reference shown in the processor's back office.</summary>
    public string MerchantReference => $"eshop-order-{OrderId}-{AttemptNumber}";

    public bool IsTerminal => Status is PaymentAttemptStatus.Authorised
        or PaymentAttemptStatus.Refused
        or PaymentAttemptStatus.Reversed
        or PaymentAttemptStatus.ReversalUnknown;

    /// <summary>True when the attempt's outcome must be settled (re-sent with the same key) before anything else happens.</summary>
    public bool NeedsSettlement(DateTimeOffset now) =>
        Status == PaymentAttemptStatus.Unknown
        || (Status == PaymentAttemptStatus.InFlight && now - UpdatedDate > InFlightLease);

    public void MarkResending(DateTimeOffset now)
    {
        Status = PaymentAttemptStatus.InFlight;
        UpdatedDate = now;
    }

    public void MarkAuthorised(string pspReference, string resultCode, DateTimeOffset now)
    {
        PspReference = pspReference;
        ResultCode = resultCode;
        Status = PaymentAttemptStatus.Authorised;
        UpdatedDate = now;
    }

    public void MarkRefused(string? pspReference, string? resultCode, string? refusalReason, string? refusalReasonCode, DateTimeOffset now)
    {
        PspReference = pspReference;
        ResultCode = resultCode;
        RefusalReason = refusalReason;
        RefusalReasonCode = refusalReasonCode;
        Status = PaymentAttemptStatus.Refused;
        UpdatedDate = now;
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = PaymentAttemptStatus.Unknown;
        UpdatedDate = now;
    }

    public void MarkReversed(string? pspReference, string? resultCode, bool reversalConfirmed, string reason, DateTimeOffset now)
    {
        PspReference = pspReference;
        ResultCode = resultCode;
        RefusalReason = reason;
        Status = reversalConfirmed ? PaymentAttemptStatus.Reversed : PaymentAttemptStatus.ReversalUnknown;
        UpdatedDate = now;
    }
}

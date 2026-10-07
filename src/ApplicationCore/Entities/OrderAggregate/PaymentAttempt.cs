using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// One request to charge the order total. Its key (<see cref="OrderId"/>, <see cref="AttemptNumber"/>) is the
/// claim that stops two concurrent "pay" calls from both reaching the payment provider.
/// </summary>
public class PaymentAttempt
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentAttempt() {}

    internal PaymentAttempt(int orderId, int attemptNumber, string idempotencyKey, string reference,
        decimal amount, long amountInMinorUnits, string currency, DateTimeOffset createdAt, int? settlesAttemptNumber)
    {
        Guard.Against.OutOfRange(attemptNumber, nameof(attemptNumber), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(reference, nameof(reference));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amountInMinorUnits, nameof(amountInMinorUnits));

        OrderId = orderId;
        AttemptNumber = attemptNumber;
        IdempotencyKey = idempotencyKey;
        Reference = reference;
        Amount = amount;
        AmountInMinorUnits = amountInMinorUnits;
        Currency = currency;
        CreatedAt = createdAt;
        SettlesAttemptNumber = settlesAttemptNumber;
        Status = PaymentAttemptStatus.InFlight;
    }

    public int OrderId { get; private set; }
    public int AttemptNumber { get; private set; }

    /// <summary>Sent to the provider as its idempotency key; reused verbatim when an unknown outcome is settled.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>The merchant reference sent with the payment.</summary>
    public string Reference { get; private set; }

    public decimal Amount { get; private set; }
    public long AmountInMinorUnits { get; private set; }
    public string Currency { get; private set; }
    public PaymentAttemptStatus Status { get; private set; }

    /// <summary>Set when this attempt re-sends an earlier attempt whose outcome was unknown.</summary>
    public int? SettlesAttemptNumber { get; private set; }

    public string? PspReference { get; private set; }
    public string? ResultCode { get; private set; }
    public string? RefusalReason { get; private set; }
    public string? RefusalReasonCode { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// An attempt claimed so long ago that the request which claimed it cannot still be running (it crashed or
    /// was killed). It is settled like an unknown outcome instead of blocking the order forever.
    /// </summary>
    public bool IsStaleClaim(DateTimeOffset now, TimeSpan staleAfter) =>
        Status == PaymentAttemptStatus.InFlight && now - CreatedAt >= staleAfter;

    internal void Complete(PaymentAttemptStatus status, string? pspReference, string? resultCode,
        string? refusalReason, string? refusalReasonCode, string? errorCode, string? errorMessage, DateTimeOffset now)
    {
        Status = status;
        PspReference = pspReference ?? PspReference;
        ResultCode = resultCode;
        RefusalReason = refusalReason;
        RefusalReasonCode = refusalReasonCode;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        CompletedAt = status == PaymentAttemptStatus.Unknown ? null : now;
    }

    internal void MarkSuperseded(DateTimeOffset now)
    {
        Status = PaymentAttemptStatus.Superseded;
        CompletedAt = now;
    }
}

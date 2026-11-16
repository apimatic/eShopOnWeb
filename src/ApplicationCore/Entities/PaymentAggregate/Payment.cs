using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Payment state for an order. This is the local projection of the state Finix owns:
/// the authorization (hold), the capture transfer, and every refund transfer.
/// No card data is stored here - only Finix token ids and non-sensitive card metadata.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    private readonly List<Refund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, string currency, long amountCents)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));

        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        AmountCents = amountCents;
        AuthorizationIdempotencySeed = Guid.NewGuid().ToString("N");
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Provider { get; private set; } = "Finix";
    public PaymentStatus Status { get; private set; } = PaymentStatus.AwaitingAuthorization;
    public string Currency { get; private set; }

    /// <summary>
    /// Unique per-payment seed used to build Finix idempotency keys. This keeps retries of
    /// the same payment idempotent without colliding when order ids are reused (for example
    /// after an in-memory database restart).
    /// </summary>
    public string AuthorizationIdempotencySeed { get; private set; }

    /// <summary>The order total in the smallest currency unit (cents).</summary>
    public long AmountCents { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public string? CaptureTransferId { get; private set; }
    public string? CaptureState { get; private set; }

    public long? CapturedAmountCents { get; private set; }
    public long? FeeCents { get; private set; }
    public long? NetCents { get; private set; }
    public long RefundedAmountCents { get; private set; }

    public int? SavedPaymentMethodId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    /// <summary>
    /// Number of authorization attempts that reached a definitive failure. It is part of
    /// the Finix idempotency key so a retry with a different card is not collapsed into
    /// the previous declined attempt, while a double-click on the same attempt is.
    /// </summary>
    public int AuthorizationAttemptCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public long RefundableAmountCents => Math.Max(0, (CapturedAmountCents ?? 0) - RefundedAmountCents);

    public void BeginAuthorizationAttempt()
    {
        FailureCode = null;
        FailureMessage = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordAuthorization(string authorizationId, string authorizationState,
        string? cardBrand, string? cardLast4, int? savedPaymentMethodId)
    {
        AuthorizationId = authorizationId;
        AuthorizationState = authorizationState;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        SavedPaymentMethodId = savedPaymentMethodId;
        FailureCode = null;
        FailureMessage = null;
        Status = PaymentStatus.Authorized;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordAuthorizationFailure(string? failureCode, string? failureMessage)
    {
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        Status = PaymentStatus.Failed;
        AuthorizationAttemptCount++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordAuthorizationState(string? authorizationState, string? transferId)
    {
        AuthorizationState = authorizationState;
        if (!string.IsNullOrWhiteSpace(transferId) && string.IsNullOrWhiteSpace(CaptureTransferId))
        {
            CaptureTransferId = transferId;
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCapture(string transferId, string captureState,
        long capturedAmountCents, long feeCents, long netCents)
    {
        CaptureTransferId = transferId;
        CaptureState = captureState;
        CapturedAmountCents = capturedAmountCents;
        FeeCents = feeCents;
        NetCents = netCents;
        Status = RefundedAmountCents > 0
            ? (RefundedAmountCents >= capturedAmountCents ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded)
            : PaymentStatus.Captured;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCaptureState(string captureState, long? feeCents = null)
    {
        CaptureState = captureState;
        if (feeCents.HasValue)
        {
            FeeCents = feeCents.Value;
            NetCents = Math.Max(0, (CapturedAmountCents ?? 0) - feeCents.Value);
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordVoid(string? authorizationState = null)
    {
        if (authorizationState != null)
        {
            AuthorizationState = authorizationState;
        }
        Status = PaymentStatus.Voided;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordFailedCapture(string? failureCode, string? failureMessage)
    {
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Refund AddRefund(long amountCents, string idempotencyKey)
    {
        var refund = new Refund(Id, amountCents, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }

    public void ApplyRefundResult(Refund refund, string providerTransferId, string status, bool succeeded)
    {
        if (succeeded)
        {
            refund.MarkSucceeded(providerTransferId, status);
            RefundedAmountCents += refund.AmountCents;
            Status = RefundedAmountCents >= (CapturedAmountCents ?? 0)
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
        }
        else
        {
            refund.MarkFailed(status);
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Refund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
}

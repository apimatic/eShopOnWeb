using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Payment state owned by the application and mirrored from Finix. The Finix
/// identifiers and states are stored so later operations (capture, void,
/// refund and reconciliation) can act on the payment that was started by an
/// earlier request.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    private readonly List<Refund> _refunds = new();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId,
        int amountCents,
        string currency,
        string finixPaymentInstrumentId,
        string finixAuthorizationId,
        string authorizationState,
        DateTimeOffset? authorizationExpiresAt,
        int authorizationAttempt)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(finixPaymentInstrumentId, nameof(finixPaymentInstrumentId));
        Guard.Against.NullOrEmpty(finixAuthorizationId, nameof(finixAuthorizationId));

        OrderId = orderId;
        AmountCents = amountCents;
        Currency = currency;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        FinixAuthorizationId = finixAuthorizationId;
        AuthorizationState = authorizationState;
        AuthorizationExpiresAt = authorizationExpiresAt;
        AuthorizationAttempt = authorizationAttempt;
        Status = PaymentStatus.Authorized;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public int AmountCents { get; private set; }
    public string Currency { get; private set; }
    public string FinixPaymentInstrumentId { get; private set; }
    public string FinixAuthorizationId { get; private set; }
    public string AuthorizationState { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int AuthorizationAttempt { get; private set; }
    public string? FinixTransferId { get; private set; }
    public string? CaptureState { get; private set; }
    public int CapturedAmountCents { get; private set; }
    public int FeeCents { get; private set; }
    public int NetCents { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public static Payment CreateFailed(int orderId,
        int amountCents,
        string currency,
        string finixPaymentInstrumentId,
        int authorizationAttempt,
        string? failureCode,
        string? failureMessage)
    {
        var payment = new Payment
        {
            OrderId = orderId,
            AmountCents = amountCents,
            Currency = currency,
            FinixPaymentInstrumentId = finixPaymentInstrumentId,
            FinixAuthorizationId = string.Empty,
            AuthorizationState = "FAILED",
            AuthorizationAttempt = authorizationAttempt,
            Status = PaymentStatus.Failed,
            FailureCode = failureCode,
            FailureMessage = failureMessage,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return payment;
    }

    public void RecordAuthorization(string authorizationId, string state, DateTimeOffset? expiresAt, string paymentInstrumentId, int? authorizationAttempt = null)
    {
        FinixAuthorizationId = authorizationId;
        AuthorizationState = state;
        AuthorizationExpiresAt = expiresAt;
        FinixPaymentInstrumentId = paymentInstrumentId;
        if (authorizationAttempt.HasValue)
        {
            AuthorizationAttempt = authorizationAttempt.Value;
        }
        Status = PaymentStatus.Authorized;
        FailureCode = null;
        FailureMessage = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordFailure(string? failureCode, string? failureMessage, int? authorizationAttempt = null)
    {
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        if (authorizationAttempt.HasValue)
        {
            AuthorizationAttempt = authorizationAttempt.Value;
        }
        Status = PaymentStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCapture(string transferId, string captureState, int capturedAmountCents, int feeCents, int netCents)
    {
        Guard.Against.NullOrEmpty(transferId, nameof(transferId));
        Guard.Against.Negative(capturedAmountCents, nameof(capturedAmountCents));
        Guard.Against.Negative(feeCents, nameof(feeCents));

        FinixTransferId = transferId;
        CaptureState = captureState;
        CapturedAmountCents = capturedAmountCents;
        FeeCents = feeCents;
        NetCents = netCents;
        Status = PaymentStatus.Captured;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordVoid(string authorizationState)
    {
        AuthorizationState = authorizationState;
        Status = PaymentStatus.Voided;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Refund AddRefund(string finixRefundId, string parentTransferId, int amountCents, string state, string idempotencyKey)
    {
        var refund = new Refund(Id, finixRefundId, parentTransferId, amountCents, state, idempotencyKey);
        _refunds.Add(refund);

        var totalRefunded = 0;
        foreach (var item in _refunds)
        {
            if (!string.Equals(item.State, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                totalRefunded += item.AmountCents;
            }
        }

        Status = totalRefunded == 0
            ? PaymentStatus.Captured
            : totalRefunded >= CapturedAmountCents && CapturedAmountCents > 0
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
        UpdatedAt = DateTimeOffset.UtcNow;
        return refund;
    }

    public int RefundedAmountCents()
    {
        var total = 0;
        foreach (var item in _refunds)
        {
            if (!string.Equals(item.State, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                total += item.AmountCents;
            }
        }
        return total;
    }
}

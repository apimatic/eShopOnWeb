using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money side of one <see cref="OrderAggregate.Order"/>: the hold, the capture and the refunds,
/// carrying the provider's ids and statuses so any later request can act on them.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, string currency, decimal amount, string invoiceId, DateTimeOffset now)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));

        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        Amount = amount;
        InvoiceId = invoiceId;
        Provider = "PayPal";
        Status = PaymentStatus.NotStarted;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Provider { get; private set; }
    public string Currency { get; private set; }
    /// <summary>The order total held/charged, fixed when the authorization is requested.</summary>
    public decimal Amount { get; private set; }
    /// <summary>Invoice id sent to the provider for the current attempt (unique per attempt).</summary>
    public string InvoiceId { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Instrument (never full card details)
    public int? PaymentMethodId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLastDigits { get; private set; }

    // Hold
    public string? AuthorizationRequestId { get; private set; }
    public string? ProviderOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? OriginalAuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? ReauthorizedAt { get; private set; }

    // Capture
    public string? CaptureRequestId { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // Release of the hold
    public string? VoidRequestId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool HasCapture => CaptureId is not null && CapturedAmount is not null;

    /// <summary>What can still be refunded: captured minus every refund that has not definitively failed.</summary>
    public decimal RefundableAmount =>
        HasCapture ? CapturedAmount!.Value - _refunds.Where(r => r.ReservesFunds).Sum(r => r.Amount) : 0m;

    public decimal RefundedAmount => _refunds.Where(r => r.Status == PaymentRefundStatus.Completed).Sum(r => r.Amount);

    /// <summary>When the current hold was placed (the original authorization, or its latest renewal).</summary>
    public DateTimeOffset? CurrentHoldPlacedAt => ReauthorizedAt ?? AuthorizedAt;

    public bool CanStartAuthorization => Status is PaymentStatus.NotStarted or PaymentStatus.Declined or PaymentStatus.AuthorizationExpired;

    public void BeginAuthorization(string requestId, string invoiceId, decimal amount, int? paymentMethodId, string? cardLastDigits, DateTimeOffset now)
    {
        if (!CanStartAuthorization)
            throw new PaymentConflictException($"Payment for order {OrderId} is {Status}; a new authorization cannot be started.");

        Guard.Against.NullOrEmpty(requestId, nameof(requestId));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        AuthorizationRequestId = requestId;
        InvoiceId = invoiceId;
        Amount = amount;
        PaymentMethodId = paymentMethodId;
        CardLastDigits = cardLastDigits;
        CardBrand = null;
        ProviderOrderId = null;
        AuthorizationId = null;
        OriginalAuthorizationId = null;
        AuthorizationStatus = null;
        AuthorizedAt = null;
        AuthorizationExpiresAt = null;
        ReauthorizedAt = null;
        LastError = null;
        Status = PaymentStatus.AuthorizationPending;
        UpdatedAt = now;
    }

    /// <returns>true when funds are now held for exactly <see cref="Amount"/>.</returns>
    public bool RecordAuthorization(ProviderAuthorization authorization, DateTimeOffset now)
    {
        RequireStatus(PaymentStatus.AuthorizationPending);
        ProviderOrderId = authorization.ProviderOrderId;
        AuthorizationId = authorization.AuthorizationId;
        OriginalAuthorizationId = authorization.AuthorizationId;
        AuthorizationStatus = authorization.ProviderStatus;
        AuthorizationExpiresAt = authorization.ExpiresAt;
        CardBrand = authorization.CardBrand ?? CardBrand;
        CardLastDigits = authorization.CardLastDigits ?? CardLastDigits;
        UpdatedAt = now;

        if (authorization.Outcome is AuthorizationOutcome.Approved or AuthorizationOutcome.Pending)
        {
            if (authorization.Amount is { } held && held != Amount)
            {
                Status = PaymentStatus.Declined;
                LastError = $"PayPal held {held} {Currency} but the order total is {Amount} {Currency}.";
                return false;
            }

            AuthorizedAt = authorization.CreatedAt ?? now;
            Status = PaymentStatus.Authorized;
            LastError = null;
            return true;
        }

        Status = PaymentStatus.Declined;
        LastError = $"PayPal did not approve the authorization (status {authorization.ProviderStatus}).";
        return false;
    }

    public void RecordAuthorizationFailure(string reason, DateTimeOffset now)
    {
        RequireStatus(PaymentStatus.AuthorizationPending);
        Status = PaymentStatus.Declined;
        LastError = reason;
        UpdatedAt = now;
    }

    public void UpdateAuthorizationState(ProviderAuthorizationState state, DateTimeOffset now)
    {
        AuthorizationStatus = state.ProviderStatus;
        AuthorizationExpiresAt = state.ExpiresAt ?? AuthorizationExpiresAt;
        UpdatedAt = now;
    }

    public void RecordReauthorization(ProviderAuthorizationState renewed, DateTimeOffset now)
    {
        RequireStatus(PaymentStatus.Authorized);
        AuthorizationId = renewed.AuthorizationId;
        AuthorizationStatus = renewed.ProviderStatus;
        AuthorizationExpiresAt = renewed.ExpiresAt ?? AuthorizationExpiresAt;
        ReauthorizedAt = renewed.CreatedAt ?? now;
        UpdatedAt = now;
    }

    public void MarkAuthorizationExpired(string reason, DateTimeOffset now)
    {
        if (Status is not (PaymentStatus.Authorized or PaymentStatus.CapturePending or PaymentStatus.VoidPending))
            throw new PaymentConflictException($"Payment for order {OrderId} is {Status}; it holds no authorization.");
        Status = PaymentStatus.AuthorizationExpired;
        LastError = reason;
        UpdatedAt = now;
    }

    public void BeginCapture(string requestId, DateTimeOffset now)
    {
        RequireStatus(PaymentStatus.Authorized);
        CaptureRequestId = requestId;
        Status = PaymentStatus.CapturePending;
        UpdatedAt = now;
    }

    public void RecordCapture(ProviderCapture capture, DateTimeOffset now)
    {
        if (Status is not (PaymentStatus.CapturePending or PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
            throw new PaymentConflictException($"Payment for order {OrderId} is {Status}; a capture cannot be recorded.");

        CaptureId = capture.CaptureId;
        CaptureStatus = capture.ProviderStatus;
        CapturedAmount = capture.Amount ?? CapturedAmount ?? Amount;
        PayPalFee = capture.PayPalFee ?? PayPalFee;
        NetAmount = capture.NetAmount ?? NetAmount;
        CapturedAt ??= now;
        AuthorizationStatus = "CAPTURED";
        if (Status == PaymentStatus.CapturePending)
            Status = PaymentStatus.Captured;
        LastError = null;
        UpdatedAt = now;
        RecomputeRefundStatus();
    }

    /// <summary>The provider refused the capture; the hold is still in place.</summary>
    public void RevertCapture(string reason, DateTimeOffset now)
    {
        RequireStatus(PaymentStatus.CapturePending);
        Status = PaymentStatus.Authorized;
        LastError = reason;
        UpdatedAt = now;
    }

    public void BeginVoid(string requestId, DateTimeOffset now)
    {
        RequireStatus(PaymentStatus.Authorized);
        VoidRequestId = requestId;
        Status = PaymentStatus.VoidPending;
        UpdatedAt = now;
    }

    public void RecordVoid(ProviderAuthorizationState state, DateTimeOffset now)
    {
        RequireStatus(PaymentStatus.VoidPending);
        AuthorizationStatus = state.ProviderStatus;
        VoidedAt = now;
        Status = PaymentStatus.Voided;
        LastError = null;
        UpdatedAt = now;
    }

    public void RevertVoid(string reason, DateTimeOffset now)
    {
        RequireStatus(PaymentStatus.VoidPending);
        Status = PaymentStatus.Authorized;
        LastError = reason;
        UpdatedAt = now;
    }

    public PaymentRefund? FindRefund(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Reserves <paramref name="amount"/> of the captured funds for a refund. A partly refunded payment
    /// can never be refunded beyond what was captured.
    /// </summary>
    public PaymentRefund AddRefund(string idempotencyKey, decimal amount, string providerRequestId, DateTimeOffset now)
    {
        if (!HasCapture || Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
            throw new PaymentConflictException($"Payment for order {OrderId} is {Status}; only captured payments with a refundable balance can be refunded.");
        if (amount <= 0)
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        if (amount > RefundableAmount)
            throw new PaymentConflictException(
                $"Refund of {amount} {Currency} exceeds the refundable balance of {RefundableAmount} {Currency} (captured {CapturedAmount} {Currency}).",
                "REFUND_EXCEEDS_CAPTURED");
        if (FindRefund(idempotencyKey) is not null)
            throw new PaymentConflictException($"A refund with idempotency key '{idempotencyKey}' already exists.");

        var refund = new PaymentRefund(idempotencyKey, amount, providerRequestId, now);
        _refunds.Add(refund);
        UpdatedAt = now;
        return refund;
    }

    public void RecordRefund(PaymentRefund refund, ProviderRefund result, DateTimeOffset now)
    {
        EnsureOwns(refund);
        refund.Record(result.RefundId, result.ProviderStatus,
            completed: result.Outcome == RefundOutcome.Completed,
            failed: result.Outcome == RefundOutcome.Failed,
            now);
        UpdatedAt = now;
        RecomputeRefundStatus();
    }

    public void FailRefund(PaymentRefund refund, string reason, DateTimeOffset now)
    {
        EnsureOwns(refund);
        refund.Fail(reason);
        UpdatedAt = now;
        RecomputeRefundStatus();
    }

    private void RecomputeRefundStatus()
    {
        if (!HasCapture || Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
            return;

        var refunded = RefundedAmount;
        Status = refunded <= 0 ? PaymentStatus.Captured
            : refunded >= CapturedAmount!.Value ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }

    private void EnsureOwns(PaymentRefund refund)
    {
        if (!_refunds.Contains(refund))
            throw new InvalidOperationException("Refund does not belong to this payment.");
    }

    private void RequireStatus(PaymentStatus expected)
    {
        if (Status != expected)
            throw new PaymentConflictException($"Payment for order {OrderId} is {Status}, expected {expected}.");
    }
}

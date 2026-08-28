using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money side of one order. It carries enough of the state PayPal owns — the ids and current
/// status of the hold, the capture and each refund — that a later request can act on the payment
/// without having to re-derive anything from the request that created it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns this payment; every shopper-facing read is scoped to it.</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>The order total this payment is for, to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; } = PaymentStatus.PendingAuthorization;

    /// <summary>Our own reference, sent to PayPal as the invoice id and echoed back in reporting.</summary>
    public string InvoiceId { get; private set; }

    public string? PayPalOrderId { get; private set; }

    public string? AuthorizationId { get; private set; }

    /// <summary>The authorization status verbatim from PayPal (e.g. <c>CREATED</c>).</summary>
    public string? AuthorizationStatus { get; private set; }

    /// <summary>When the current hold goes stale. Drives whether fulfilment must renew it first.</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>
    /// Bumped before every attempt to place a hold — a first authorization, a retry after a
    /// decline, or a re-authorization. It discriminates the idempotency keys sent to PayPal, so a
    /// deliberate second attempt is not deduplicated against the one it replaced, while an
    /// accidental replay of the same attempt still is.
    /// </summary>
    public int AuthorizationAttempt { get; private set; }

    /// <summary>
    /// Set when a call's outcome could not be established — the money may or may not be held. No
    /// further attempt is allowed until reconciliation settles it, because retrying blind is how a
    /// shopper ends up holding two authorizations.
    /// </summary>
    public bool AwaitingReconciliation { get; private set; }

    public string? CaptureId { get; private set; }

    /// <summary>The capture status verbatim from PayPal (e.g. <c>COMPLETED</c>).</summary>
    public string? CaptureStatus { get; private set; }

    public decimal? CapturedAmount { get; private set; }

    /// <summary>PayPal's fee on the capture, as PayPal reported it.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>What the merchant nets after PayPal's fee, as PayPal reported it.</summary>
    public decimal? NetAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, decimal amount, string currencyCode, string invoiceId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        InvoiceId = invoiceId;
    }

    /// <summary>Money actually returned so far (refunds the processor cancelled or failed do not count).</summary>
    public decimal TotalRefunded =>
        _refunds.Where(r => r.ReducesRefundableBalance).Sum(r => r.Amount);

    /// <summary>What is still refundable. Never exceeds what was captured.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    public bool IsAuthorizationStale(DateTimeOffset now) =>
        AuthorizationExpiresAt.HasValue && AuthorizationExpiresAt.Value <= now;

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Opens an attempt to place a hold and returns its number, which callers fold into the
    /// idempotency keys they send to the processor.
    /// </summary>
    public int BeginAuthorizationAttempt()
    {
        GuardReconciliation();

        if (Status is not (PaymentStatus.PendingAuthorization or PaymentStatus.Failed))
        {
            throw new OrderStateException(
                $"Payment {Id} for order {OrderId} is already {Status}; it cannot be authorized again.");
        }

        Status = PaymentStatus.PendingAuthorization;
        AuthorizationAttempt++;
        Touch();
        return AuthorizationAttempt;
    }

    /// <summary>Opens an attempt to renew a stale hold, returning the attempt number.</summary>
    public int BeginReauthorizationAttempt()
    {
        GuardReconciliation();
        RequireStatus(PaymentStatus.Authorized, "re-authorize");

        AuthorizationAttempt++;
        Touch();
        return AuthorizationAttempt;
    }

    /// <summary>Records that a call's outcome is unknown, freezing the payment until it is reconciled.</summary>
    public void MarkOutcomeUnknown()
    {
        AwaitingReconciliation = true;
        Touch();
    }

    /// <summary>Clears the freeze once an operator has established what actually happened.</summary>
    public void ClearReconciliationHold()
    {
        AwaitingReconciliation = false;
        Touch();
    }

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string status,
        DateTimeOffset? expiresAt)
    {
        RequireStatus(PaymentStatus.PendingAuthorization, "record an authorization for");

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Replaces a stale hold with the fresh one PayPal issued in its place.</summary>
    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        RequireStatus(PaymentStatus.Authorized, "re-authorize");

        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        RequireStatus(PaymentStatus.Authorized, "capture");

        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void MarkVoided(string? authorizationStatus = null)
    {
        RequireStatus(PaymentStatus.Authorized, "void");

        AuthorizationStatus = authorizationStatus ?? AuthorizationStatus;
        Status = PaymentStatus.Voided;
        Touch();
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
        Touch();
    }

    /// <summary>
    /// Records a refund and moves the payment to partially or fully refunded. Rejects anything that
    /// would take the total refunded past what was captured, so a partly-refunded payment can never
    /// become refundable beyond its capture.
    /// </summary>
    public PaymentRefund AddRefund(string idempotencyKey, string payPalRefundId, string status, decimal amount)
    {
        if (Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new OrderStateException(
                $"Payment {Id} for order {OrderId} is {Status}; only a captured payment can be refunded.");
        }

        var refund = new PaymentRefund(idempotencyKey, payPalRefundId, status, amount, CurrencyCode);
        _refunds.Add(refund);

        // A refund the processor cancelled or failed returned no money, so the payment is still
        // simply captured — marking it refunded would understate what is still owed to the shopper.
        if (refund.ReducesRefundableBalance)
        {
            Status = RefundableRemaining <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        }

        Touch();
        return refund;
    }

    /// <summary>
    /// Guards a refund request before it reaches the processor. Returns the amount to refund —
    /// the caller's amount, or the whole remaining balance when they asked for a full refund.
    /// </summary>
    public decimal ValidateRefundAmount(decimal? requestedAmount)
    {
        if (Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new OrderStateException(
                $"Payment {Id} for order {OrderId} is {Status}; only a captured payment can be refunded.");
        }

        var remaining = RefundableRemaining;
        if (remaining <= 0m)
        {
            throw new PaymentValidationException(
                $"Order {OrderId} has already been refunded in full ({TotalRefunded:0.00} {CurrencyCode}); " +
                "there is nothing left to refund.");
        }

        if (requestedAmount is null)
        {
            return remaining;
        }

        if (requestedAmount.Value <= 0m)
        {
            throw new PaymentValidationException("A refund amount must be greater than zero.");
        }

        if (requestedAmount.Value > remaining)
        {
            throw new PaymentValidationException(
                $"Refunding {requestedAmount.Value:0.00} {CurrencyCode} would exceed what is still " +
                $"refundable on order {OrderId} ({remaining:0.00} {CurrencyCode} of a " +
                $"{CapturedAmount:0.00} {CurrencyCode} capture).");
        }

        return requestedAmount.Value;
    }

    private void GuardReconciliation()
    {
        if (AwaitingReconciliation)
        {
            throw new OrderStateException(
                $"Payment {Id} for order {OrderId} has an unsettled outcome at the payment processor. " +
                "Reconcile it (GET /api/reconciliation) before attempting another payment operation, " +
                "so the shopper is not charged or held twice.");
        }
    }

    private void RequireStatus(PaymentStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new OrderStateException(
                $"Payment {Id} for order {OrderId} cannot {action} because it is {Status}; it must be {expected}.");
        }
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

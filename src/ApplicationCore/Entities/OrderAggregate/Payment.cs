using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The money movement attached to an <see cref="Order"/>. Part of the Order aggregate.
/// Holds enough of the state PayPal owns — the ids and current status of the hold
/// (authorization), the capture, and each refund — that a later request can act on it,
/// and enforces the legal transitions between those states.
/// </summary>
public class Payment : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.PendingAuthorization;
        IdempotencySeed = Guid.NewGuid();
    }

    /// <summary>
    /// A per-payment unique seed for building stable, globally-unique PayPal-Request-Id idempotency
    /// keys. Stable for the payment's life (so a retry reuses the same key) yet unique across
    /// payments — so keys never collide even if a store re-uses order ids (e.g. the in-memory DB
    /// after a restart).
    /// </summary>
    public Guid IdempotencySeed { get; private set; }

    /// <summary>The order total to hold/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO-4217 currency code, from configuration.</summary>
    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    // ---- State PayPal owns: the hold (order + authorization) ----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // ---- State PayPal owns: the capture (money taken) ----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public bool IsAwaitingPayment => Status == PaymentStatus.PendingAuthorization;
    public bool IsAuthorized => Status == PaymentStatus.Authorized;
    public bool IsCaptured => Status is PaymentStatus.Captured
        or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded;

    /// <summary>Sum of refunds that have not failed/cancelled — pending refunds count so the
    /// capture cannot be over-refunded while a refund is in flight.</summary>
    public decimal TotalRefunded => _refunds.Where(r => !r.IsUnsuccessful).Sum(r => r.Amount);

    /// <summary>How much of the capture may still be refunded.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    /// <summary>True when the hold has an expiry that is already in the past.</summary>
    public bool IsAuthorizationStale(DateTimeOffset asOf) =>
        AuthorizationExpiresAt.HasValue && AuthorizationExpiresAt.Value <= asOf;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        if (!IsAwaitingPayment)
            throw new PaymentOperationException($"Order cannot be authorized while its payment is '{Status}'.");

        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replaces the hold's id/status/expiry after a stale authorization is renewed.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        if (!IsAuthorized)
            throw new PaymentOperationException($"Only an authorized payment can be renewed; this one is '{Status}'.");

        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        if (!IsAuthorized)
            throw new PaymentOperationException($"Order cannot be fulfilled while its payment is '{Status}'.");

        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        if (!IsAuthorized)
            throw new PaymentOperationException($"Order cannot be cancelled while its payment is '{Status}'. " +
                "Only an order that is holding funds (authorized) but not yet fulfilled can be cancelled.");

        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>Returns a prior refund made under the same idempotency key, if any.</summary>
    public Refund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Starts a new refund of the capture. <paramref name="amount"/> null means the whole
    /// remaining refundable amount. Guarded so the total refunded can never exceed what was
    /// captured.
    /// </summary>
    public Refund StartRefund(string idempotencyKey, decimal? amount)
    {
        if (!IsCaptured)
            throw new PaymentOperationException($"Only a fulfilled (captured) order can be refunded; this one is '{Status}'.");

        var refundAmount = amount ?? RefundableRemaining;
        if (refundAmount <= 0m)
            throw new PaymentOperationException("Refund amount must be greater than zero.");
        if (refundAmount > RefundableRemaining)
            throw new PaymentOperationException(
                $"Refund of {refundAmount:0.00} exceeds the {RefundableRemaining:0.00} still refundable on this capture.");

        var refund = new Refund(idempotencyKey, refundAmount);
        _refunds.Add(refund);
        return refund;
    }

    /// <summary>Records PayPal's outcome for a refund and rolls the payment's status forward.</summary>
    public void CompleteRefund(Refund refund, string payPalRefundId, RefundStatus status)
    {
        Guard.Against.Null(refund, nameof(refund));
        refund.SetResult(payPalRefundId, status);

        if (TotalRefunded >= (CapturedAmount ?? 0m) && TotalRefunded > 0m)
            Status = PaymentStatus.Refunded;
        else if (TotalRefunded > 0m)
            Status = PaymentStatus.PartiallyRefunded;
    }
}

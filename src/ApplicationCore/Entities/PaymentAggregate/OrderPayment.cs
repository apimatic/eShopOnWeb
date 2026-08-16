using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the money movement and PayPal-owned state for a single <c>Order</c>. Kept as a
/// separate aggregate so the payment capability is purely additive to the existing order model.
/// Carries enough PayPal state (ids and status for the hold, the capture and the refunds) that a
/// later request can act on the payment without replaying the one that started it.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, decimal amount, string currencyCode,
        string reconciliationReference)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(reconciliationReference, nameof(reconciliationReference));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        ReconciliationReference = reconciliationReference;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    /// <summary>The order total this payment is for (equals the PayPal hold amount to the cent).</summary>
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>Stable, unique reference carried to PayPal (invoice_id / custom_id) for reconciliation.</summary>
    public string ReconciliationReference { get; private set; }

    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // ----- PayPal-owned hold (authorization) state -----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // ----- PayPal-owned capture state -----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGrossAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    /// <summary>The saved card used to authorize, when the shopper paid with one of their vaulted cards.</summary>
    public int? SavedPaymentMethodId { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool IsAuthorized => AuthorizationId is not null;
    public bool IsCaptured => CaptureId is not null;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replaces the hold details after a stale authorization is renewed (reauthorized).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal grossAmount,
        decimal payPalFee, decimal netAmount, DateTimeOffset? capturedAt)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGrossAmount = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void MarkCancelled()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Cancelled;
    }

    /// <summary>Sum of all refunds that committed money against the capture.</summary>
    public decimal TotalRefunded() => _refunds.Where(r => r.CountsTowardRefunded).Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining() => (CapturedGrossAmount ?? 0m) - TotalRefunded();

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public PaymentRefund AddRefund(string idempotencyKey, decimal amount, string? payPalRefundId,
        string status, string? noteToPayer)
    {
        var refund = new PaymentRefund(Id, idempotencyKey, amount, payPalRefundId, status, noteToPayer);
        _refunds.Add(refund);

        var totalRefunded = TotalRefunded();
        if (CapturedGrossAmount.HasValue && totalRefunded >= CapturedGrossAmount.Value)
        {
            Status = PaymentStatus.Refunded;
        }
        else if (totalRefunded > 0m)
        {
            Status = PaymentStatus.PartiallyRefunded;
        }

        return refund;
    }
}

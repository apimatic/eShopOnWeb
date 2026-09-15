using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the money movement and fulfilment state for a single eShop <see cref="OrderAggregate.Order"/>.
/// This is an additive aggregate: the base Order is left untouched and each Order has exactly one Payment.
/// It carries enough of the state PayPal owns (ids and statuses for the hold, the capture and the refunds)
/// that a later request can act on it, not only the one that started it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, string currencyCode, decimal amount, string customId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(customId, nameof(customId));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
        CustomId = customId;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;

        // Stable per-payment idempotency keys, generated once so a double-click reuses the same
        // PayPal-Request-Id and PayPal de-duplicates the operation instead of repeating it.
        AuthorizeRequestId = Guid.NewGuid().ToString("N");
        CaptureRequestId = Guid.NewGuid().ToString("N");
        VoidRequestId = Guid.NewGuid().ToString("N");
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>The order total captured at pay time — the exact amount PayPal must hold.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>Merchant-owned reconciliation key echoed to PayPal (surfaces as custom_field in reporting).</summary>
    public string CustomId { get; private set; }

    // ---- PayPal-owned state ----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>The saved card used to pay, when the shopper paid with one (Flow 2).</summary>
    public int? SavedPaymentMethodId { get; private set; }

    public string AuthorizeRequestId { get; private set; }
    public string CaptureRequestId { get; private set; }
    public string VoidRequestId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    /// <summary>Amount still available to refund without exceeding what was captured.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, int? savedPaymentMethodId)
    {
        if (Status != PaymentStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Order {OrderId} cannot be authorized while its payment is {Status}.");
        }

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = PaymentStatus.Authorized;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records a renewed authorization (e.g. after a stale hold was re-authorized before capture).</summary>
    public void UpdateAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkFulfilled(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {OrderId} cannot be fulfilled while its payment is {Status}.");
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Fulfilled;
        FulfilledAt = DateTimeOffset.UtcNow;
    }

    public void MarkCancelled()
    {
        if (Status != PaymentStatus.AwaitingPayment && Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Order {OrderId} cannot be cancelled while its payment is {Status}. Fulfilled orders must be refunded instead.");
        }

        Status = PaymentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        if (AuthorizationStatus is not null)
        {
            AuthorizationStatus = "VOIDED";
        }
    }

    public PaymentRefund AddRefund(Guid id, decimal amount, string idempotencyKey, string? payPalRefundId, string status)
    {
        var refund = new PaymentRefund(id, amount, idempotencyKey, payPalRefundId, status);
        _refunds.Add(refund);

        Status = TotalRefunded >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }
}

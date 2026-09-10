using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment and fulfilment state for a single <see cref="OrderAggregate.Order"/>. eShop's own
/// <c>Order</c> carries no payment state, so this additive aggregate holds enough of the state PayPal
/// owns — the hold, the capture, and the refunds, each with its id and current status — that a later
/// request can act on it, not only the one that started it.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, decimal amount, string currencyCode)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        CurrencyCode = currencyCode;
        Status = PaymentStatus.AwaitingPayment;
        // A globally-unique reference for PayPal's invoice id and idempotency (request) ids. The order id
        // alone is not unique across process restarts under the in-memory provider; this always is.
        PaymentReference = $"ESHOP-{orderId}-{Guid.NewGuid():N}";
    }

    /// <summary>The stable eShop reference PayPal echoes as custom_id, used for reconciliation.</summary>
    public string ReconciliationReference => $"ESHOP-{OrderId}";

    /// <summary>A globally-unique reference used as PayPal's invoice id and idempotency-key stem.</summary>
    public string PaymentReference { get; private set; }

    /// <summary>The eShop order this payment is for.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order and payment.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total, to the cent.</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- State PayPal owns ---

    /// <summary>PayPal's order id (the container the authorization/capture live under).</summary>
    public string? ProcessorOrderId { get; private set; }

    /// <summary>PayPal's authorization id — the hold on the funds.</summary>
    public string? AuthorizationId { get; private set; }

    /// <summary>PayPal's reported authorization status.</summary>
    public string? AuthorizationStatus { get; private set; }

    /// <summary>When the current authorization goes stale and must be renewed before capture.</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>PayPal's capture id — created at fulfilment.</summary>
    public string? CaptureId { get; private set; }

    /// <summary>PayPal's reported capture status.</summary>
    public string? CaptureStatus { get; private set; }

    /// <summary>The amount PayPal actually captured.</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>PayPal's fee on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>The net proceeds to the merchant after PayPal's fee.</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>A safe description of the funding card, e.g. "VISA ending 1111".</summary>
    public string? PaymentMethodDescription { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    // --- Behaviour ---

    /// <summary>Records a successful authorization (the money is held, not taken).</summary>
    public void MarkAuthorized(string processorOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? paymentMethodDescription)
    {
        Guard.Against.NullOrEmpty(processorOrderId, nameof(processorOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        ProcessorOrderId = processorOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        PaymentMethodDescription = paymentMethodDescription;
        PaidAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replaces the authorization with a renewed one (reauthorization before fulfilment).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records the capture reported by PayPal at fulfilment (the money is now taken).</summary>
    public void MarkFulfilled(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = decimal.Round(capturedAmount, 2, MidpointRounding.AwayFromZero);
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        FulfilledAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Fulfilled;
    }

    /// <summary>Records the release of held funds when the order is cancelled before fulfilment.</summary>
    public void MarkCancelled()
    {
        AuthorizationStatus = "VOIDED";
        CancelledAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Cancelled;
    }

    /// <summary>The sum of every refund already issued against the capture.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>Returns the refund already recorded under this idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>Records a refund and advances the status to partially or fully refunded.</summary>
    public PaymentRefund AddRefund(string idempotencyKey, string refundId, decimal amount, string status)
    {
        var refund = new PaymentRefund(idempotencyKey, refundId, amount, status);
        _refunds.Add(refund);

        Status = RefundableRemaining() <= 0m
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }
}

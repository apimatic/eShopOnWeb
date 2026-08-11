using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the money movement for a single <see cref="OrderAggregate.Order"/>. One order has one
/// <see cref="OrderPayment"/>. It carries enough of the state PayPal owns (order id, authorization
/// id/status/expiry, capture id/status, per-capture fee &amp; net, and the list of refunds) that a later
/// request can act on it, not only the request that created it.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>Owner of the order/payment (the shopper's identity from the JWT). Denormalised for ownership checks.</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>The order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    public OrderPaymentStatus Status { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    // ----- PayPal-owned state -----

    /// <summary>The PayPal Checkout order id (POST /v2/checkout/orders).</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>Unique invoice id sent to PayPal; the key used to line PayPal's transaction records up against this order.</summary>
    public string? InvoiceId { get; private set; }

    /// <summary>The PayPal authorization id (the hold); used to capture/void/reauthorize.</summary>
    public string? AuthorizationId { get; private set; }

    public string? AuthorizationStatus { get; private set; }

    /// <summary>When the current authorization expires; used to detect a stale hold before fulfilment.</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>The PayPal capture id (money taken); used to refund.</summary>
    public string? CaptureId { get; private set; }

    public string? CaptureStatus { get; private set; }

    /// <summary>Amount PayPal actually captured (gross), as reported at fulfilment.</summary>
    public decimal? CapturedGross { get; private set; }

    /// <summary>PayPal's fee on the capture, as reported at fulfilment.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant (gross - fee), as reported at fulfilment.</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>Safe description of the payment instrument used (e.g. "VISA ending 1111"). Never full card details.</summary>
    public string? PaymentInstrumentDescription { get; private set; }

    /// <summary>Stable idempotency key sent to PayPal for the authorize call so a double-click cannot authorize twice.</summary>
    public string AuthorizeRequestId { get; private set; } = Guid.NewGuid().ToString("N");

    /// <summary>Stable idempotency key sent to PayPal for the capture call so a double-click cannot capture twice.</summary>
    public string CaptureRequestId { get; private set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, decimal amount, string currencyCode)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
    }

    public bool IsAuthorized => AuthorizationId is not null &&
        Status is OrderPaymentStatus.Authorized;

    public bool IsCaptured => CaptureId is not null &&
        Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded;

    /// <summary>Total value refunded so far against the capture.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured amount is still refundable.</summary>
    public decimal RefundableRemaining() => (CapturedGross ?? Amount) - TotalRefunded();

    /// <summary>True once the hold has passed its expiry and must be renewed before it can be captured.</summary>
    public bool IsAuthorizationStale(DateTimeOffset now) =>
        AuthorizationExpiresAt.HasValue && AuthorizationExpiresAt.Value <= now;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? instrumentDescription, string? invoiceId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        if (Status is not (OrderPaymentStatus.AwaitingPayment or OrderPaymentStatus.Failed))
        {
            throw new PaymentException($"Order {OrderId} cannot be authorized because its payment is {Status}.");
        }

        PayPalOrderId = payPalOrderId;
        InvoiceId = invoiceId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        PaymentInstrumentDescription = instrumentDescription;
        Status = OrderPaymentStatus.Authorized;
        Touch();
    }

    public void MarkFailed()
    {
        Status = OrderPaymentStatus.Failed;
        Touch();
    }

    /// <summary>Replace the authorization id/status/expiry after a stale hold has been renewed (reauthorize).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status is not OrderPaymentStatus.Authorized)
        {
            throw new PaymentException($"Order {OrderId} authorization cannot be renewed because its payment is {Status}.");
        }
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void RefreshAuthorizationStatus(string authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationStatus = authorizationStatus;
        if (expiresAt.HasValue)
        {
            AuthorizationExpiresAt = expiresAt;
        }
        Touch();
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal gross, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status is not OrderPaymentStatus.Authorized)
        {
            throw new PaymentException($"Order {OrderId} cannot be fulfilled because its payment is {Status}, not Authorized.");
        }
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGross = gross;
        PayPalFee = fee;
        NetAmount = net;
        Status = OrderPaymentStatus.Fulfilled;
        Touch();
    }

    public void MarkVoided()
    {
        if (Status is OrderPaymentStatus.Canceled)
        {
            return; // idempotent
        }
        if (Status is not (OrderPaymentStatus.Authorized or OrderPaymentStatus.AwaitingPayment))
        {
            throw new PaymentException($"Order {OrderId} cannot be cancelled because its payment is {Status}. Cancellation is only possible before fulfilment.");
        }
        AuthorizationStatus = "VOIDED";
        Status = OrderPaymentStatus.Canceled;
        Touch();
    }

    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        if (Status is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException($"Order {OrderId} cannot be refunded because its payment is {Status}. A refund is only possible after fulfilment.");
        }

        if (amount - RefundableRemaining() > 0.0001m)
        {
            throw new PaymentException(
                $"Refund of {amount:0.00} {CurrencyCode} exceeds the refundable remaining amount of {RefundableRemaining():0.00} {CurrencyCode} for order {OrderId}.");
        }

        var refund = new PaymentRefund(payPalRefundId, amount, CurrencyCode, status, idempotencyKey);
        _refunds.Add(refund);

        Status = RefundableRemaining() <= 0.0001m
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
        Touch();
        return refund;
    }

    /// <summary>Look up an existing refund by the caller-supplied idempotency key (for replayed requests).</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment that settles a catalog <see cref="OrderAggregate.Order"/>. It is its own aggregate
/// root (one per order) so it can carry the money-movement lifecycle and the PayPal-owned state
/// (order/authorization/capture/refund ids and their current status) that later requests act on,
/// without changing the existing order model.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>The authorized order total, in <see cref="CurrencyCode"/>.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>Unique merchant reference (PayPal invoice id) used to reconcile against PayPal's records.</summary>
    public string MerchantReference { get; private set; }

    /// <summary>Optional saved card used to pay (null for a one-off card).</summary>
    public int? PaymentMethodId { get; private set; }

    // Idempotency keys — generated once and reused so retries of the same logical action
    // never authorize, capture or void twice.
    public string AuthorizationRequestId { get; private set; }
    public string CaptureRequestId { get; private set; }
    public string VoidRequestId { get; private set; }

    // PayPal-owned state.
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGross { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.Now;
    public DateTimeOffset? AuthorizedDate { get; private set; }
    public DateTimeOffset? CapturedDate { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, decimal amount, string currencyCode, string merchantReference)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(merchantReference, nameof(merchantReference));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        MerchantReference = merchantReference;
        Status = PaymentStatus.PendingAuthorization;

        AuthorizationRequestId = Guid.NewGuid().ToString("N");
        CaptureRequestId = Guid.NewGuid().ToString("N");
        VoidRequestId = Guid.NewGuid().ToString("N");
    }

    public bool IsAuthorized => Status == PaymentStatus.Authorized;
    public bool IsCaptured => Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded;

    /// <summary>Records a successful authorization (a hold on funds).</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus,
        DateTimeOffset? expiresAt, int? paymentMethodId)
    {
        if (Status is not (PaymentStatus.PendingAuthorization or PaymentStatus.Authorized))
            throw new PaymentException($"Cannot authorize a payment in status {Status}.");

        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        PaymentMethodId = paymentMethodId;
        AuthorizedDate = DateTimeOffset.Now;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replaces a stale authorization with a renewed one (same order, new hold).</summary>
    public void RenewAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        if (Status != PaymentStatus.Authorized)
            throw new PaymentException($"Cannot renew the authorization of a payment in status {Status}.");

        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records that the held funds have been captured (taken) at fulfilment.</summary>
    public void MarkCaptured(string captureId, string? captureStatus, decimal capturedGross, decimal? fee, decimal? net)
    {
        if (Status != PaymentStatus.Authorized)
            throw new PaymentException($"Cannot capture a payment in status {Status}.");

        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGross = capturedGross;
        PayPalFee = fee;
        NetAmount = net;
        CapturedDate = DateTimeOffset.Now;
        Status = PaymentStatus.Captured;
    }

    /// <summary>Records that the authorization was voided before fulfilment (funds released).</summary>
    public void MarkVoided(string? authorizationStatus)
    {
        if (Status != PaymentStatus.Authorized)
            throw new PaymentException($"Cannot cancel a payment in status {Status}.");

        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Voided;
    }

    /// <summary>Sum of refunds that actually returned money.</summary>
    public decimal TotalRefunded() => _refunds.Where(r => r.IsEffective).Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining() => (CapturedGross ?? 0m) - TotalRefunded();

    /// <summary>Returns an already-applied refund for the given idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>Records a refund against the capture, enforcing that refunds never exceed what was captured.</summary>
    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        if (Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
            throw new PaymentException($"Cannot refund a payment in status {Status}.");

        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (amount > RefundableRemaining())
            throw new PaymentException("Refund amount exceeds the remaining refundable balance.");

        var refund = new PaymentRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = RefundableRemaining() <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}

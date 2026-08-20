using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the money movement for a single <see cref="OrderAggregate.Order"/>. It is an aggregate root in
/// its own right (it is never mutated except through its own methods) and it carries enough of the state
/// PayPal owns — the ids and current status of the hold, the capture and the refunds — that a later request
/// can act on it rather than only the request that created it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, string currencyCode, decimal amount)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
        Status = PaymentStatus.AwaitingPayment;
        // Globally-unique invoice/reference for this order instance. PayPal requires invoice ids to be unique
        // per transaction, and it also anchors the idempotency keys so a retry is stable while two different
        // order instances never collide.
        InvoiceId = $"eshop-order-{orderId}-{Guid.NewGuid():N}";
        CreatedDate = DateTimeOffset.UtcNow;
        LastUpdatedDate = CreatedDate;
    }

    public int OrderId { get; private set; }

    /// <summary>Unique invoice/reference sent to PayPal for this order; also the base for idempotency keys.</summary>
    public string InvoiceId { get; private set; }

    /// <summary>The shopper who owns this payment (their username). Used to scope shopper access.</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>The authorized order total, to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    // ---- State owned by PayPal ----

    /// <summary>PayPal's order id (created with intent=AUTHORIZE).</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>PayPal's authorization id — the hold.</summary>
    public string? AuthorizationId { get; private set; }

    /// <summary>PayPal's last-known status for the authorization.</summary>
    public string? AuthorizationStatus { get; private set; }

    /// <summary>PayPal's capture id — set once the money is taken.</summary>
    public string? CaptureId { get; private set; }

    /// <summary>PayPal's last-known status for the capture.</summary>
    public string? CaptureStatus { get; private set; }

    /// <summary>The amount PayPal actually captured.</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>The fee PayPal charged on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>The net proceeds to the merchant after PayPal's fee.</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>A human-readable note about the last failure, for an operator to act on.</summary>
    public string? LastError { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset LastUpdatedDate { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Records the PayPal order id created for this payment (before the hold is placed).</summary>
    public void SetPayPalOrder(string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
        Touch();
    }

    /// <summary>Records that PayPal is now holding the funds.</summary>
    public void MarkAuthorized(string authorizationId, string authorizationStatus)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Authorized;
        LastError = null;
        Touch();
    }

    /// <summary>Records that a stale hold was renewed under a new authorization id.</summary>
    public void MarkAuthorizationRenewed(string newAuthorizationId, string authorizationStatus)
    {
        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        AuthorizationId = newAuthorizationId;
        AuthorizationStatus = authorizationStatus;
        Touch();
    }

    /// <summary>Records that the hold was released without any money moving.</summary>
    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
        Touch();
    }

    /// <summary>Records what PayPal reported when the money was taken.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));
        Guard.Against.Negative(capturedAmount, nameof(capturedAmount));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
        LastError = null;
        Touch();
    }

    public void MarkFailed(string error)
    {
        Status = PaymentStatus.Failed;
        LastError = error;
        Touch();
    }

    public void RecordError(string error)
    {
        LastError = error;
        Touch();
    }

    /// <summary>Sum of every refund taken against the capture.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    public bool TryGetRefundByKey(string idempotencyKey, out PaymentRefund? refund)
    {
        refund = _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        return refund is not null;
    }

    /// <summary>
    /// Adds a refund that has already succeeded at PayPal, guarding that the total refunded can never exceed
    /// what was captured, and advancing the payment status to partially/fully refunded.
    /// </summary>
    public PaymentRefund AddRefund(string idempotencyKey, decimal amount, string payPalRefundId, string status)
    {
        if (Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new InvalidOperationException($"Order {OrderId} cannot be refunded from status {Status}.");
        }

        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (amount > RefundableRemaining())
        {
            throw new InvalidOperationException(
                $"Refund of {amount} exceeds the remaining refundable amount {RefundableRemaining()} for order {OrderId}.");
        }

        var refund = new PaymentRefund(idempotencyKey, amount, payPalRefundId, status);
        _refunds.Add(refund);

        Status = RefundableRemaining() <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        Touch();
        return refund;
    }

    private void Touch() => LastUpdatedDate = DateTimeOffset.UtcNow;
}

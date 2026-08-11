using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment attached to an order. Carries enough of the state PayPal owns — the ids and
/// current status of the hold (authorization), the capture, and any refunds — that a later
/// request can act on it, not only the one that created it. One payment exists per order and
/// is created, in <see cref="PaymentStatus.AwaitingPayment"/>, the moment the order is placed.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
        IdempotencyToken = Guid.NewGuid().ToString("N");
    }

    public int OrderId { get; private set; }

    /// <summary>
    /// A stable, globally-unique token for this payment. It namespaces the PayPal-Request-Id of every
    /// money-moving call so that a double-click is de-duplicated at PayPal, while payments from
    /// different orders or app runs (order ids restart each in-memory run) never collide.
    /// </summary>
    public string IdempotencyToken { get; private set; }

    /// <summary>The shopper who owns this payment (username). Never acted on by anyone else.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- State PayPal owns ---

    /// <summary>The PayPal Checkout order id created when authorizing.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>The PayPal authorization id backing the current hold on funds.</summary>
    public string? AuthorizationId { get; private set; }

    /// <summary>PayPal's current status for the authorization (e.g. CREATED, CAPTURED, VOIDED, EXPIRED).</summary>
    public string? AuthorizationStatus { get; private set; }

    /// <summary>The PayPal capture id created at fulfilment.</summary>
    public string? CaptureId { get; private set; }

    /// <summary>PayPal's current status for the capture (e.g. COMPLETED).</summary>
    public string? CaptureStatus { get; private set; }

    /// <summary>The gross amount PayPal actually captured.</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>The fee PayPal charged the merchant on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>The net proceeds to the merchant after PayPal's fee.</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>When the capture was taken, used to place the transaction within a reconciliation range.</summary>
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    /// <summary>Total value returned to the shopper across all recorded refunds.</summary>
    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured amount is still available to refund.</summary>
    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    /// <summary>Records a successful authorization (a hold placed on the money).</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus)
    {
        if (Status != PaymentStatus.AwaitingPayment)
            throw new InvalidOperationException($"Cannot authorize a payment in status {Status}.");

        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>
    /// Replaces the authorization with a freshly reauthorized one when the original hold went
    /// stale before fulfilment. The new id is what subsequent captures must use.
    /// </summary>
    public void MarkReauthorized(string newAuthorizationId, string authorizationStatus)
    {
        if (Status != PaymentStatus.Authorized)
            throw new InvalidOperationException($"Cannot reauthorize a payment in status {Status}.");

        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        AuthorizationId = newAuthorizationId;
        AuthorizationStatus = authorizationStatus;
    }

    public void RefreshAuthorizationStatus(string authorizationStatus) => AuthorizationStatus = authorizationStatus;

    /// <summary>Records the capture taken at fulfilment, with what PayPal reported.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        if (Status != PaymentStatus.Authorized)
            throw new InvalidOperationException($"Cannot capture a payment in status {Status}.");

        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    /// <summary>Records that the hold was released before fulfilment; no money moved.</summary>
    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized)
            throw new InvalidOperationException($"Cannot cancel a payment in status {Status}.");

        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>
    /// Finds an already-recorded refund made under the same idempotency key, if any, so a repeated
    /// request returns the original result instead of refunding twice.
    /// </summary>
    public Refund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Books a new refund of the captured payment. Guards that the total refunded can never exceed
    /// what was captured, so a partly-refunded order is never refundable beyond its capture.
    /// </summary>
    public Refund AddRefund(string idempotencyKey, decimal amount)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
            throw new InvalidOperationException($"Cannot refund a payment in status {Status}.");

        if (amount > RefundableAmount)
            throw new InvalidOperationException(
                $"Refund of {amount:0.00} exceeds the refundable balance of {RefundableAmount:0.00} {Currency}.");

        var refund = new Refund(idempotencyKey, amount);
        _refunds.Add(refund);
        return refund;
    }

    /// <summary>Updates the payment status after a refund settles (full vs partial).</summary>
    public void ApplyRefundOutcome()
    {
        Status = RefundableAmount <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }
}

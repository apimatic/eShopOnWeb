using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment for a single eShop <c>Order</c> (1:1). It carries enough of the state
/// PayPal owns — the ids and current status of the hold, the capture, and the refunds —
/// that a later request can act on it, not only the one that started it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, string currency, decimal amount)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        Amount = amount;
        Status = PaymentStatus.AwaitingPayment;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this payment is for.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order/payment. Used for ownership scoping.</summary>
    public string BuyerId { get; private set; }

    /// <summary>ISO-4217 currency code (from configuration).</summary>
    public string Currency { get; private set; }

    /// <summary>The order total that is authorized/held. Equals the order total to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>PayPal Checkout order id created during authorization.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>PayPal authorization id (the hold). May be renewed (reauthorized).</summary>
    public string? AuthorizationId { get; private set; }

    /// <summary>PayPal capture id (created at fulfilment).</summary>
    public string? CaptureId { get; private set; }

    /// <summary>Amount PayPal reported captured.</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>PayPal's fee on the capture.</summary>
    public decimal? PaypalFee { get; private set; }

    /// <summary>Net proceeds to the merchant after PayPal's fee.</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>Running total of money returned to the shopper.</summary>
    public decimal RefundedAmount { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset? UpdatedDate { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Records the money hold. Only valid from <see cref="PaymentStatus.AwaitingPayment"/>.</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.AwaitingPayment)
            throw new PaymentOperationException($"Order {OrderId} cannot be authorized from state {Status}.");

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Replaces the authorization id after a stale hold has been renewed (reauthorized).</summary>
    public void RenewAuthorization(string newAuthorizationId)
    {
        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        if (Status != PaymentStatus.Authorized)
            throw new PaymentOperationException($"Order {OrderId} authorization cannot be renewed from state {Status}.");
        AuthorizationId = newAuthorizationId;
        Touch();
    }

    /// <summary>Records the capture (money taken). Only valid from <see cref="PaymentStatus.Authorized"/>.</summary>
    public void MarkCaptured(string captureId, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
            throw new PaymentOperationException($"Order {OrderId} cannot be fulfilled from state {Status}.");

        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
        Touch();
    }

    /// <summary>Releases the hold before fulfilment. Only valid from <see cref="PaymentStatus.Authorized"/>.</summary>
    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized)
            throw new PaymentOperationException($"Order {OrderId} cannot be cancelled from state {Status}.");
        Status = PaymentStatus.Voided;
        Touch();
    }

    /// <summary>The amount still eligible to be refunded (captured minus already refunded).</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - RefundedAmount;

    /// <summary>Returns the refund previously made under this idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>Guards a refund request before it is sent to PayPal.</summary>
    public void EnsureRefundable(decimal amount)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
            throw new PaymentOperationException($"Order {OrderId} cannot be refunded from state {Status}.");
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (amount > RefundableRemaining())
            throw new PaymentOperationException(
                $"Refund of {amount} exceeds the {RefundableRemaining()} still refundable on order {OrderId}.");
    }

    /// <summary>Records a completed refund and advances the status.</summary>
    public PaymentRefund AddRefund(string refundId, decimal amount, string idempotencyKey)
    {
        EnsureRefundable(amount);
        var refund = new PaymentRefund(refundId, amount, Currency, idempotencyKey);
        _refunds.Add(refund);
        RefundedAmount += amount;
        Status = RefundedAmount >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
        return refund;
    }

    private void Touch() => UpdatedDate = DateTimeOffset.UtcNow;
}

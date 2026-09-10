using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment for an <see cref="OrderAggregate.Order"/>. Holds the state PayPal owns —
/// the checkout order id, the current authorization id (which changes on reauthorization),
/// the capture id and settlement breakdown, and the refunds — so any later request can act
/// on the payment rather than only the one that started it. There is at most one Payment
/// per Order.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string invoiceId, string currency, decimal amount, string payPalOrderId,
        string authorizationId, int? paymentMethodId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        OrderId = orderId;
        InvoiceId = invoiceId;
        Currency = currency;
        Amount = amount;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        PaymentMethodId = paymentMethodId;
        Status = PaymentStatus.Authorized;
    }

    public int OrderId { get; private set; }

    /// <summary>
    /// The invoice id sent to PayPal, tying PayPal's transactions back to this order during
    /// reconciliation. Unique per app-run so it never collides with a prior run's ids.
    /// </summary>
    public string InvoiceId { get; private set; }

    public string Currency { get; private set; }

    /// <summary>The authorized amount. Equals the order total to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The PayPal v2 checkout order id.</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>The current PayPal authorization id. Replaced on reauthorization.</summary>
    public string AuthorizationId { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>The PayPal capture id, once the payment has been captured at fulfilment.</summary>
    public string? CaptureId { get; private set; }

    /// <summary>Amount PayPal reported as captured (gross).</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>The fee PayPal charged on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds credited to the merchant (gross minus fee).</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>The saved card used to pay, if any (otherwise a one-off card was used).</summary>
    public int? PaymentMethodId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    /// <summary>Sum of all non-failed refunds against this payment.</summary>
    public decimal TotalRefunded => _refunds.Where(r => r.Status != RefundStatus.Failed).Sum(r => r.Amount);

    /// <summary>Amount still available to refund (captured minus already refunded).</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    /// <summary>Replaces the authorization id after a successful reauthorization.</summary>
    public void ApplyReauthorization(string newAuthorizationId)
    {
        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        AuthorizationId = newAuthorizationId;
    }

    public void MarkCaptured(string captureId, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        Status = PaymentStatus.Voided;
    }

    /// <summary>
    /// Records a refund and advances the payment status. Guards against refunding more than
    /// was captured so a partly-refunded payment never becomes refundable beyond the capture.
    /// </summary>
    public Refund AddRefund(string payPalRefundId, decimal amount, string idempotencyKey, RefundStatus status)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException($"Payment for order {OrderId} is '{Status}' and cannot be refunded; only a captured payment can be refunded.");
        }

        if (amount > RefundableRemaining)
        {
            throw new PaymentException(
                $"Refund of {amount:0.00} {Currency} exceeds the {RefundableRemaining:0.00} {Currency} remaining on the captured payment for order {OrderId}.");
        }

        var refund = new Refund(payPalRefundId, amount, idempotencyKey, status);
        _refunds.Add(refund);

        if (TotalRefunded >= (CapturedAmount ?? 0m))
        {
            Status = PaymentStatus.Refunded;
        }
        else
        {
            Status = PaymentStatus.PartiallyRefunded;
        }

        return refund;
    }

    public Refund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
    }
}

using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment and fulfilment state for a single <see cref="OrderAggregate.Order"/>. This is additive: it
/// holds the money-movement state the base order model never carried, plus the PayPal-owned identifiers
/// (order/authorization/capture/refund ids and their current statuses) needed for a later request to act on
/// the payment — not only the one that started it. There is one <see cref="OrderPayment"/> per order,
/// keyed by <see cref="OrderId"/>, owned by the shopper named in <see cref="BuyerId"/>.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, string currency, decimal amount)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.Negative(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        Amount = amount;
        Status = PaymentStatus.AwaitingPayment;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }

    /// <summary>The order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>Stable per-order reference sent to PayPal as invoice_id/custom_id; used for reconciliation.</summary>
    public string? InvoiceReference { get; private set; }

    // PayPal-owned identifiers / statuses for the hold.
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? AuthorizationExpiresAt { get; private set; }

    // ...for the capture.
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>Sum of all refunds taken; never exceeds <see cref="CapturedAmount"/>.</summary>
    public decimal RefundedAmount { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool IsAwaitingPayment => Status == PaymentStatus.AwaitingPayment;
    public bool IsAuthorized => Status == PaymentStatus.Authorized;
    public bool IsFulfilled => Status is PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded;

    /// <summary>Amount still eligible to be refunded (captured minus already refunded).</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - RefundedAmount;

    public void SetInvoiceReference(string invoiceReference)
    {
        Guard.Against.NullOrEmpty(invoiceReference, nameof(invoiceReference));
        InvoiceReference ??= invoiceReference;
    }

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus, string? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replace the authorization after a renewal (reauthorize gives a new authorization id).</summary>
    public void RenewAuthorization(string authorizationId, string? authorizationStatus, string? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkAuthorizationFailed()
    {
        Status = PaymentStatus.Failed;
    }

    public void MarkFulfilled(string captureId, string? captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        Status = PaymentStatus.Cancelled;
    }

    /// <summary>
    /// Records a refund and recomputes the refunded total and status. Callers must first check
    /// <see cref="RefundableRemaining"/>; this method enforces the same invariant defensively so a
    /// partly-refunded order can never become refundable beyond what was captured.
    /// </summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (refund.Amount > RefundableRemaining)
        {
            throw new System.InvalidOperationException(
                $"Refund of {refund.Amount} exceeds the refundable remaining {RefundableRemaining} for order {OrderId}.");
        }

        _refunds.Add(refund);
        RefundedAmount = _refunds.Sum(r => r.Amount);
        Status = RefundedAmount >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }

    /// <summary>Find a prior refund taken under the given idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

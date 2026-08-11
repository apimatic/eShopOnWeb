using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Carries the PayPal money-movement state for a single eShop <c>Order</c>: the hold (authorization),
/// the capture, and any refunds — enough of the state PayPal owns (ids + current status) that a later
/// request can act on it. This is an additive aggregate that references the existing order by id; it
/// does not replace or duplicate the order/order-item model.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currencyCode, string invoiceId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        InvoiceId = invoiceId;
        Status = PaymentStatus.AwaitingPayment;
    }

    /// <summary>The eShop order this payment settles.</summary>
    public int OrderId { get; private set; }

    /// <summary>
    /// The PayPal <c>invoice_id</c> carried on this order's charges — unique per run (so it never
    /// collides across in-memory restarts) yet stored so reconciliation can line PayPal's transactions
    /// up against this order.
    /// </summary>
    public string InvoiceId { get; private set; }

    /// <summary>The owning shopper (username/email), used to scope every operation to its owner.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total to hold/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    public PaymentStatus Status { get; private set; }

    // ---- Hold (authorization) ----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    // ---- Capture ----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    /// <summary>A safe, human-readable description of the instrument used (e.g. "VISA ****1111"). Never full card data.</summary>
    public string? PaymentMethodDescription { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>Amount still refundable against the capture; never exceeds what was captured.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, string? paymentMethodDescription)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAt = DateTimeOffset.Now;
        PaymentMethodDescription = paymentMethodDescription;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replace the authorization after a stale one is renewed (reauthorized) at PayPal.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAt = DateTimeOffset.Now;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.Now;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void MarkCanceled()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Canceled;
    }

    /// <summary>
    /// Record a refund. Enforces that total refunds never exceed the captured amount, then advances the
    /// status to <see cref="PaymentStatus.PartiallyRefunded"/> or <see cref="PaymentStatus.Refunded"/>.
    /// </summary>
    public Refund AddRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var refund = new Refund(idempotencyKey, payPalRefundId, amount, status);
        _refunds.Add(refund);

        Status = TotalRefunded() >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }

    public Refund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment record for an <see cref="OrderAggregate.Order"/>. It is additive state
/// attached to the existing order model and carries enough of the state PayPal owns
/// (the hold, the capture and the refunds — their ids and current status) that a later
/// request can act on the payment, not only the one that started it.
/// One payment belongs to exactly one order and to the shopper who placed it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currencyCode)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Amount = Guard.Against.NegativeOrZero(amount, nameof(amount));
        CurrencyCode = Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Status = PaymentStatus.AwaitingPayment;
        CreatedDate = DateTimeOffset.UtcNow;
        // Globally-unique, retry-stable seed for PayPal idempotency (PayPal-Request-Id). The
        // integer key restarts at 1 on each in-memory run and would collide with prior
        // idempotency records on a shared account, so operation keys derive from this GUID.
        IdempotencyToken = Guid.NewGuid().ToString("N");
    }

    // ----- identity / ownership -----
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    /// <summary>Per-payment seed for PayPal idempotency keys; unique and stable across retries.</summary>
    public string IdempotencyToken { get; private set; }

    // ----- money -----
    /// <summary>The order total to authorize/capture, in <see cref="CurrencyCode"/>.</summary>
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }

    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset? UpdatedDate { get; private set; }

    /// <summary>Unique invoice id sent to PayPal; the key used to reconcile PayPal transactions to this order.</summary>
    public string? InvoiceId { get; private set; }

    // ----- PayPal state: the hold -----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // ----- PayPal state: the capture -----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // ----- safe descriptor of the instrument that paid (never full card details) -----
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>
    /// Reserves the unique invoice id that will be sent to PayPal. Persisted before the
    /// authorization call so a retried payment reuses the same id (and PayPal de-duplicates)
    /// rather than opening a second PayPal order under a different invoice.
    /// </summary>
    public void PrepareInvoice(string invoiceId)
    {
        if (Status != PaymentStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {OrderId} is not awaiting payment (status: {Status}).");
        }

        if (string.IsNullOrEmpty(InvoiceId))
        {
            InvoiceId = Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
            Touch();
        }
    }

    /// <summary>Records a successful authorization (money held, not taken).</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId,
        string authorizationStatus, DateTimeOffset? authorizationExpiresAt, string? cardBrand, string? cardLast4)
    {
        if (Status != PaymentStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {OrderId} is not awaiting payment (status: {Status}).");
        }

        if (string.IsNullOrEmpty(InvoiceId))
        {
            throw new PaymentException($"Order {OrderId} has no reserved invoice id.");
        }

        PayPalOrderId = Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Replaces the live authorization after a renewal (reauthorize).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? authorizationExpiresAt)
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentException($"Order {OrderId} has no live authorization to renew (status: {Status}).");
        }

        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        Touch();
    }

    /// <summary>Records that the money was taken at fulfilment, including PayPal's reported breakdown.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentException($"Order {OrderId} cannot be fulfilled because it is not authorized (status: {Status}).");
        }

        CaptureId = Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
        Touch();
    }

    /// <summary>Records that the held funds were released before fulfilment (no money moved).</summary>
    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentException($"Order {OrderId} cannot be cancelled because it is not awaiting fulfilment (status: {Status}).");
        }

        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
        Touch();
    }

    /// <summary>Sum of all refunds recorded against the capture.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>Amount still available to refund against the capture.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>Returns an already-recorded refund for the given idempotency key, or null.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Validates that a refund of <paramref name="amount"/> is permissible right now,
    /// throwing a <see cref="PaymentException"/> otherwise. Called before contacting PayPal.
    /// </summary>
    public void EnsureRefundable(decimal amount)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException($"Order {OrderId} cannot be refunded because it has not been captured (status: {Status}).");
        }

        Guard.Against.NegativeOrZero(amount, nameof(amount));

        // A partly-refunded order must never become refundable beyond what was captured.
        if (amount > RefundableRemaining())
        {
            throw new PaymentException(
                $"Refund of {amount:0.00} exceeds the remaining refundable amount of {RefundableRemaining():0.00} for order {OrderId}.");
        }
    }

    /// <summary>Records a completed refund and advances the status accordingly.</summary>
    public PaymentRefund AddRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        EnsureRefundable(amount);

        var refund = new PaymentRefund(idempotencyKey, payPalRefundId, amount, status);
        _refunds.Add(refund);

        Status = TotalRefunded() >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
        return refund;
    }

    private void Touch() => UpdatedDate = DateTimeOffset.UtcNow;
}

using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money-movement state that follows an <see cref="OrderAggregate.Order"/>: the hold
/// (authorization), the capture at fulfilment, and any refunds. This aggregate owns everything the
/// provider (PayPal) reports back — ids and current statuses — so that a later request can act on
/// the payment, not only the one that started it. It never stores card details.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, string currencyCode, decimal amount, string invoiceId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
        InvoiceId = invoiceId;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>The eShop <see cref="OrderAggregate.Order"/> this payment settles.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order/payment (JWT identity/email).</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>The order total the shopper must pay, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Stable external id used to reconcile eShop orders against PayPal transactions.</summary>
    public string InvoiceId { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- PayPal-owned state (ids + current status) ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>Running total already refunded against the capture.</summary>
    public decimal RefundedAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Amount still available to refund against the capture.</summary>
    public decimal RefundableAmount => (CapturedAmount ?? 0m) - RefundedAmount;

    /// <summary>Records the hold PayPal placed on the shopper's money.</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Replaces the authorization id after a stale hold has been renewed (reauthorized).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        Touch();
    }

    /// <summary>Records the money actually taken at fulfilment, as PayPal reported it.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
        Touch();
    }

    /// <summary>Records that the hold was released before fulfilment (voided).</summary>
    public void MarkCancelled()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Cancelled;
        Touch();
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
        Touch();
    }

    /// <summary>
    /// Adds a refund taken against the capture. Enforces that a partly-refunded order never becomes
    /// refundable beyond what was captured.
    /// </summary>
    public PaymentRefund AddRefund(string refundId, decimal amount, string idempotencyKey, string status)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException("Only a captured payment can be refunded.", 409);
        }

        // Guard the invariant to the cent; floating tolerance would let a rounding drift over-refund.
        if (amount > RefundableAmount)
        {
            throw new PaymentException(
                $"Refund of {amount:0.00} exceeds the refundable amount {RefundableAmount:0.00}.", 422);
        }

        var refund = new PaymentRefund(refundId, amount, idempotencyKey, status);
        _refunds.Add(refund);
        RefundedAmount += amount;
        Status = RefundedAmount >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
        return refund;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.Find(r => r.IdempotencyKey == idempotencyKey);
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

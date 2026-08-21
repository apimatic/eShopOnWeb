using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the money movement for one <see cref="OrderAggregate.Order"/>: the hold placed at
/// checkout, the capture taken at fulfilment, and any refunds. It carries enough of the state
/// PayPal owns (its order id, authorization id, capture id and refund ids, plus their current
/// status) that a later request can act on the payment, not only the one that started it.
///
/// This is a separate aggregate from <see cref="OrderAggregate.Order"/> so the payment capability
/// is additive: the existing order/order-item model is reused unchanged, and the payment state
/// lives alongside it keyed by <see cref="OrderId"/>.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
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
        Status = PaymentStatus.AwaitingPayment;
        CreatedDate = DateTimeOffset.Now;
        // A per-payment token that makes the PayPal invoice id globally unique even though the
        // in-memory OrderId restarts at 1 on every run.
        InvoiceReference = Guid.NewGuid().ToString("N");
    }

    public int OrderId { get; private set; }

    /// <summary>Unique token used to build a collision-free PayPal invoice id for this payment.</summary>
    public string InvoiceReference { get; private set; }

    /// <summary>The shopper who owns this payment (the order's buyer id / username).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total to be authorized, in <see cref="CurrencyCode"/>.</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- state PayPal owns for the hold ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- state PayPal owns for the capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGrossAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset? UpdatedDate { get; private set; }

    /// <summary>The last caller-safe error surfaced for this payment (for operator visibility).</summary>
    public string? LastErrorMessage { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>The amount actually captured (falls back to the authorized amount if not yet read back).</summary>
    public decimal CapturedAmount => CapturedGrossAmount ?? Amount;

    /// <summary>The sum of refunds that have not failed — money that has been (or is being) returned.</summary>
    public decimal TotalRefunded() =>
        _refunds.Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.Amount);

    /// <summary>How much of the capture can still be refunded — never below zero, never above the capture.</summary>
    public decimal RefundableRemaining() => CapturedAmount - TotalRefunded();

    // --- behaviour ---

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        LastErrorMessage = null;
        Touch();
    }

    public void MarkActionRequired(string? payPalOrderId, string message)
    {
        PayPalOrderId = payPalOrderId ?? PayPalOrderId;
        Status = PaymentStatus.ActionRequired;
        LastErrorMessage = message;
        Touch();
    }

    public void MarkFailed(string message)
    {
        Status = PaymentStatus.Failed;
        LastErrorMessage = message;
        Touch();
    }

    /// <summary>Replaces a stale authorization with a renewed one (re-authorization).</summary>
    public void RenewAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidPaymentOperationException("Only an authorized payment can have its authorization renewed.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void MarkCaptured(string captureId, string? captureStatus, decimal grossAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGrossAmount = grossAmount;
        PayPalFeeAmount = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
        LastErrorMessage = null;
        Touch();
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
        LastErrorMessage = null;
        Touch();
    }

    /// <summary>
    /// Records a refund against the capture. Enforces that the total refunded never exceeds the
    /// captured amount, and recomputes the payment status (partial vs full refund).
    /// </summary>
    public PaymentRefund AddRefund(string idempotencyKey, decimal amount, string payPalRefundId, string? status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidPaymentOperationException("Only a captured payment can be refunded.");
        }

        if (amount > RefundableRemaining())
        {
            throw new InvalidPaymentOperationException(
                $"Refund of {amount:0.00} {CurrencyCode} exceeds the refundable remaining amount of {RefundableRemaining():0.00} {CurrencyCode}.");
        }

        var refund = new PaymentRefund(idempotencyKey, amount, CurrencyCode);
        refund.SetResult(payPalRefundId, status);
        _refunds.Add(refund);

        Status = RefundableRemaining() <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        Touch();
        return refund;
    }

    /// <summary>Finds an already-recorded refund for a caller-supplied idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    private void Touch() => UpdatedDate = DateTimeOffset.Now;
}

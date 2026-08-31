using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment for an order. Carries the identifiers and statuses owned by the payment
/// processor (PayPal) so that any later request — not only the one that started the
/// payment — can act on the hold, the capture and the refunds.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = OrderPaymentStatus.AwaitingPayment;

        // Merchant accounts can enforce globally unique invoice ids; the suffix keeps the
        // id unique across runs while staying stable for every retry of this payment.
        InvoiceId = $"eshop-{orderId}-{Guid.NewGuid():N}";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    /// <summary>The invoice id sent to PayPal for this payment.</summary>
    public string InvoiceId { get; private set; }

    /// <summary>The order total the processor must hold, in <see cref="Currency"/>.</summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    public OrderPaymentStatus Status { get; private set; }

    // --- PayPal-owned state: the order and the authorization (the hold) ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    // --- PayPal-owned state: the capture (money taken at fulfilment) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds
        .Where(r => r.Status != PaymentRefundStatus.Failed)
        .Sum(r => r.Amount);

    /// <summary>What can still be refunded; never more than what was captured.</summary>
    public decimal RefundableAmount => CapturedAmount.HasValue
        ? Math.Max(0m, CapturedAmount.Value - TotalRefunded)
        : 0m;

    /// <summary>Remembers the PayPal order created for this payment so a retried attempt reuses it.</summary>
    public void RecordPayPalOrderId(string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
    }

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
        Status = OrderPaymentStatus.Authorized;
    }

    /// <summary>Refreshes the hold after a reauthorization renewed it.</summary>
    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = OrderPaymentStatus.Authorized;
    }

    public void MarkAuthorizationFailed()
    {
        Status = OrderPaymentStatus.Failed;
    }

    public void MarkVoided(string? authorizationStatus)
    {
        if (Status != OrderPaymentStatus.Authorized)
        {
            throw new OrderStateException($"Payment for order {OrderId} cannot be voided while in state {Status}.");
        }
        AuthorizationStatus = authorizationStatus ?? AuthorizationStatus;
        Status = OrderPaymentStatus.Voided;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount, DateTimeOffset capturedAt)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        if (Status != OrderPaymentStatus.Authorized)
        {
            throw new OrderStateException($"Payment for order {OrderId} cannot be captured while in state {Status}.");
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        Status = OrderPaymentStatus.Captured;
    }

    public PaymentRefund AddRefund(string payPalRefundId, string refundStatus, decimal amount,
        string idempotencyKey, string? noteToPayer)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        if (Status != OrderPaymentStatus.Captured)
        {
            throw new OrderStateException($"Payment for order {OrderId} cannot be refunded while in state {Status}.");
        }
        if (FindRefundByIdempotencyKey(idempotencyKey) != null)
        {
            throw new DuplicateException($"A refund with idempotency key '{idempotencyKey}' already exists for order {OrderId}.");
        }
        if (amount > RefundableAmount)
        {
            throw new OrderStateException(
                $"Refund of {amount} {Currency} exceeds the refundable amount of {RefundableAmount} {Currency} for order {OrderId}.");
        }

        var refund = new PaymentRefund(payPalRefundId, refundStatus, amount, idempotencyKey, noteToPayer);
        _refunds.Add(refund);
        return refund;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
        => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

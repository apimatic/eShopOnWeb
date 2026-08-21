using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment for an <see cref="OrderAggregate.Order"/>. It owns the state PayPal owns — the ids and
/// current status of the hold (authorization), the capture and the refunds — so that a later request
/// (fulfil, cancel, refund, reconcile) can act on it, not only the request that started it.
/// One payment per order.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currencyCode, string payPalOrderId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        PayPalOrderId = payPalOrderId;
        Status = PaymentStatus.Created;
    }

    /// <summary>The eShop order this payment settles.</summary>
    public int OrderId { get; private set; }

    /// <summary>The owning shopper (username/email); used to scope every payment operation.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total to hold/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO-4217 currency code from configuration.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>PayPal's order id.</summary>
    public string PayPalOrderId { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- Authorization (the hold) ---
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- Capture (the money taken at fulfilment) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- Refunds ---
    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void SetAuthorized(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>
    /// A stale hold that had to be renewed before capture yields a fresh authorization id and honor period.
    /// </summary>
    public void SetReauthorized(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        // Remains authorized (renewed), not a new lifecycle state.
    }

    public void SetCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
    }

    public void SetVoided(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Voided;
    }

    /// <summary>
    /// The amount already refunded (or refund-pending) against the capture. Failed/cancelled refunds
    /// do not count, so the same amount can be retried.
    /// </summary>
    public decimal TotalRefunded() => _refunds.Where(r => r.CountsAgainstCapture).Sum(r => r.Amount);

    /// <summary>The remaining amount that may still be refunded without exceeding the captured total.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>
    /// Finds a refund previously created under the same caller idempotency key, if any. Enables the
    /// caller to repeat a refund request under one key without refunding twice.
    /// </summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Records a refund. Guards that the capture exists and that the running refunded total never
    /// exceeds what was captured, then advances the payment to Partially/fully Refunded.
    /// </summary>
    public PaymentRefund AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Payment for order {OrderId} cannot be refunded from status {Status}; only a captured payment can be refunded.");
        }
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        if (amount > RefundableRemaining())
        {
            throw new InvalidOperationException(
                $"Refund of {amount} {CurrencyCode} exceeds the refundable remaining {RefundableRemaining()} {CurrencyCode} for order {OrderId}.");
        }

        var refund = new PaymentRefund(refundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = RefundableRemaining() <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }

    /// <summary>True once the whole captured amount has been refunded.</summary>
    public bool IsFullyRefunded => Status == PaymentStatus.Refunded;
}

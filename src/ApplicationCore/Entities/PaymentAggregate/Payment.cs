using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment for an <see cref="OrderAggregate.Order"/>. Carries enough of the state PayPal
/// owns (the ids and current status of the hold, the capture and any refunds) that a later
/// request can act on the payment, not just the one that created it. One payment per order.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    /// <summary>
    /// Created when the order total is authorized (a hold is placed on the money).
    /// </summary>
    public Payment(
        int orderId,
        string buyerId,
        string currency,
        decimal amount,
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt,
        int? savedPaymentMethodId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        Amount = amount;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = PaymentStatus.Authorized;
    }

    public int OrderId { get; private set; }

    /// <summary>Owner of the payment; must match the order's buyer.</summary>
    public string BuyerId { get; private set; }

    public string Currency { get; private set; }

    /// <summary>The authorized amount — equal to the order total to the cent.</summary>
    public decimal Amount { get; private set; }

    // --- PayPal-owned state for the hold ---
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- PayPal-owned state for the capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>Set when this order was paid with one of the shopper's saved cards.</summary>
    public int? SavedPaymentMethodId { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>When the capture (money taken) happened; null until fulfilment.</summary>
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Replaces the hold with a renewed authorization (reauthorize on a stale hold).</summary>
    public void RenewAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Payment for order {OrderId} is '{Status}'; only an authorized payment can be reauthorized.");
        }
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records the capture taken at fulfilment: gross captured, PayPal's fee and net proceeds.</summary>
    public void MarkCaptured(string captureId, string status, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Payment for order {OrderId} is '{Status}'; only an authorized payment can be captured.");
        }
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    /// <summary>Releases the hold (void) when the order is cancelled before fulfilment.</summary>
    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Payment for order {OrderId} is '{Status}'; only an authorized payment can be voided.");
        }
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
        => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>
    /// Records a refund against the capture. Enforces that the running total of refunds never
    /// exceeds the captured amount, so a partly-refunded order can never become refundable
    /// beyond what was captured.
    /// </summary>
    public PaymentRefund AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException(
                $"Payment for order {OrderId} is '{Status}'; only a captured payment can be refunded.");
        }
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        // Invariant: the running total of refunds can never exceed what was captured.
        if (TotalRefunded() + amount > (CapturedAmount ?? 0m))
        {
            throw new InvalidOperationException(
                $"Refund of {amount:0.00} would exceed the {RefundableRemaining():0.00} still refundable on the payment for order {OrderId}.");
        }

        var refund = new PaymentRefund(refundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = TotalRefunded() >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The PayPal-backed payment for an <see cref="OrderAggregate.Order"/>. It carries enough of
/// the state PayPal owns (ids and current status for the hold, the capture and the refunds)
/// that a later request can act on it, not only the one that started it.
///
/// Full card details are never stored here — only a safe descriptor (brand + last 4).
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>Owning shopper identity (matches Order.BuyerId). Used to scope access.</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>Order total to be held / captured, to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    // --- PayPal-owned identifiers & state --------------------------------------------------

    /// <summary>PayPal Checkout order id (v2/checkout/orders).</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>PayPal authorization id (the hold).</summary>
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>PayPal capture id (money taken at fulfilment).</summary>
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- Safe card descriptor (never full PAN) ---------------------------------------------
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, string currencyCode, decimal amount)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    public void SetAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? cardBrand, string? cardLast4)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Replace the current hold with a renewed one (reauthorize before capture).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void SetCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? fee, decimal? net)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void SetVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
        Touch();
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
        Touch();
    }

    /// <summary>Total already refunded across all recorded refunds.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>Amount still refundable — never exceeds what was captured.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    public PaymentRefund? FindRefundByIdempotencyKey(string key) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == key);

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        Status = RefundableRemaining() <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        Touch();
    }
}

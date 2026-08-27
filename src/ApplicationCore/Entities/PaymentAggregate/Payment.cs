using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Payment for an order. Carries the state the payment provider owns (ids and current status
/// for the authorization hold, the capture, and any refunds) so that a later request can act
/// on the payment, not only the request that started it. Full card details are never stored.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() {}

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Stable, globally unique key seed for this payment's provider idempotency keys
    /// (PayPal-Request-Id). A Guid so keys can never collide with another payment's,
    /// even across database resets.
    /// </summary>
    public Guid OperationKey { get; private set; } = Guid.NewGuid();

    // Authorization (the hold)
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // Safe description of the instrument used (never a full card number)
    public string? CardBrand { get; private set; }
    public string? CardLastDigits { get; private set; }

    // Capture (money taken at fulfilment)
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool IsCaptured => CaptureId != null;

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string status,
        DateTimeOffset? expiresAt, string? cardBrand, string? cardLastDigits)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLastDigits = cardLastDigits;
    }

    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkAuthorizationVoided(string status)
    {
        AuthorizationStatus = status;
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public decimal TotalRefunded => _refunds
        .Where(r => r.Status != PaymentRefund.StatusFailed && r.Status != PaymentRefund.StatusCancelled)
        .Sum(r => r.Amount);

    public decimal RefundableBalance => (CapturedAmount ?? 0m) - TotalRefunded;

    public PaymentRefund AddRefund(string refundId, string status, decimal amount, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        if (amount > RefundableBalance)
        {
            throw new PaymentStateException(
                $"Refund of {amount} {Currency} exceeds the refundable balance {RefundableBalance} {Currency} on capture {CaptureId}.");
        }

        var refund = new PaymentRefund(refundId, status, amount, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }
}

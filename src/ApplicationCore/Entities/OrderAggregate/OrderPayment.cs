using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Carries the payment state that PayPal owns for an order: the ids and current
/// status of the hold (authorization), the capture, and the refunds — enough that a
/// later request can act on the payment, not only the one that started it.
/// This is a child of the <see cref="Order"/> aggregate.
/// </summary>
public class OrderPayment : BaseEntity
{
    public string Currency { get; private set; }

    /// <summary>The order total that must be authorized/captured, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Stable idempotency key used for the PayPal authorize call so a
    /// double-click never authorizes twice. Rotated only after a definitive decline,
    /// where no hold was placed and a genuine retry is a new attempt.</summary>
    public string AuthorizationIdempotencyKey { get; private set; }

    /// <summary>Stable idempotency key for the capture call.</summary>
    public string? CaptureIdempotencyKey { get; private set; }

    // Hold (authorization)
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // Capture
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGross { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Amount = amount;
        Currency = currency;
        AuthorizationIdempotencyKey = Guid.NewGuid().ToString("N");
    }

    /// <summary>Issues a fresh authorization idempotency key. Only safe to call when no
    /// hold exists (a prior attempt was declined), so a genuine retry is treated as new.</summary>
    public void RotateAuthorizationKey()
    {
        if (AuthorizationId is null)
        {
            AuthorizationIdempotencyKey = Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>Returns the stable capture idempotency key, creating it on first use.</summary>
    public string EnsureCaptureKey()
    {
        CaptureIdempotencyKey ??= Guid.NewGuid().ToString("N");
        return CaptureIdempotencyKey;
    }

    public void SetAuthorization(string payPalOrderId, string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records that the current authorization was replaced by a fresh one
    /// (reauthorization) before capture.</summary>
    public void ReplaceAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void SetCapture(string captureId, string status, decimal capturedGross, decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedGross = capturedGross;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
    }

    public void MarkAuthorizationVoided()
    {
        AuthorizationStatus = "VOIDED";
    }

    public decimal TotalRefunded() =>
        _refunds.Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.Amount);

    /// <summary>Amount still refundable = captured gross minus what has already been refunded.</summary>
    public decimal RemainingRefundable() => (CapturedGross ?? 0m) - TotalRefunded();

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public PaymentRefund AddRefund(string idempotencyKey, decimal amount, string? payPalRefundId, string status)
    {
        var refund = new PaymentRefund(idempotencyKey, amount, payPalRefundId, status);
        _refunds.Add(refund);
        return refund;
    }
}

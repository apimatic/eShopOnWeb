using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Carries the payment state that PayPal owns for an <see cref="Order"/> — the ids and current
/// status of the hold (authorization), the capture, and any refunds — so that a later request can
/// act on the payment, not only the one that started it. Part of the Order aggregate.
/// </summary>
public class OrderPayment
{
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    // PayPal Orders v2 resource that carries the authorization.
    public string? PayPalOrderId { get; private set; }

    // Authorization (the hold placed at pay time).
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // Capture (money taken at fulfilment).
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Amount = amount;
        Currency = currency;
    }

    public bool IsAuthorized => !string.IsNullOrEmpty(AuthorizationId);
    public bool IsCaptured => !string.IsNullOrEmpty(CaptureId);

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    /// <summary>Amount of the capture still available to refund.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    public void SetAuthorization(string payPalOrderId, string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records a renewed authorization after a stale one is reauthorized.</summary>
    public void RenewAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void SetCapture(string captureId, string status, decimal capturedAmount, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
    }

    public void MarkAuthorizationVoided()
    {
        AuthorizationStatus = "VOIDED";
    }

    /// <summary>
    /// Finds a refund previously recorded under the same caller-supplied idempotency key, if any.
    /// </summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Guards that a new refund of <paramref name="amount"/> would not exceed what was captured.
    /// A partly-refunded order must never become refundable beyond the captured total.
    /// </summary>
    public void GuardRefundWithinCaptured(decimal amount)
    {
        if (!IsCaptured)
        {
            throw new InvalidOperationException("Cannot refund an order that has not been captured.");
        }
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (amount > RefundableRemaining)
        {
            throw new InvalidOperationException(
                $"Refund amount {amount} exceeds the refundable remaining {RefundableRemaining} on capture {CaptureId}.");
        }
    }

    public PaymentRefund AddRefund(string refundId, decimal amount, string status, string idempotencyKey, DateTimeOffset createdAt)
    {
        var refund = new PaymentRefund(refundId, amount, status, idempotencyKey, createdAt);
        _refunds.Add(refund);
        return refund;
    }
}

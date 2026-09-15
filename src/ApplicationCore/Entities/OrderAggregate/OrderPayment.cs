using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment record attached to an <see cref="Order"/>. It carries enough of the state PayPal owns
/// (ids and current status for the hold, the capture and the refunds) that a later request can act on it,
/// not only the one that started it. No card details are ever stored here.
/// </summary>
public class OrderPayment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(string currencyCode, decimal amount)
    {
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        CurrencyCode = currencyCode;
        Amount = amount;
        Status = PaymentStatus.PendingAuthorization;
        IdempotencyToken = Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// A stable, globally-unique token minted when the order is placed. It seeds the PayPal-Request-Id for
    /// authorization so a double-click never authorizes twice, while distinct orders never collide (important
    /// because order ids restart per run against a shared sandbox account).
    /// </summary>
    public string IdempotencyToken { get; private set; } = Guid.NewGuid().ToString("N");

    /// <summary>Settlement currency (ISO-4217).</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>The order total to authorize/capture, snapshotted when the order was placed.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- PayPal-owned identifiers & status ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replace the authorization after a reauthorization renewed a stale hold.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount, DateTimeOffset capturedAt)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>Sum of refunds already issued that reduce the refundable balance (excludes failed/cancelled).</summary>
    public decimal TotalRefunded() => _refunds
        .Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        .Sum(r => r.Amount);

    /// <summary>How much of the capture can still be refunded. Never negative.</summary>
    public decimal RefundableRemaining()
    {
        var remaining = (CapturedAmount ?? 0m) - TotalRefunded();
        return remaining > 0m ? remaining : 0m;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        Status = RefundableRemaining() <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }
}

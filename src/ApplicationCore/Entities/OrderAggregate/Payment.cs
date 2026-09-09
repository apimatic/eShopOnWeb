using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The money movement for an order, carrying enough of the state PayPal owns (ids and current
/// status for the hold, the capture and the refunds) that a later request can act on it — not only
/// the request that started it. Part of the Order aggregate; persisted with the order.
/// </summary>
public class Payment : BaseEntity
{
    public int OrderId { get; private set; }

    /// <summary>Currency the payment is denominated in (from configuration).</summary>
    public string Currency { get; private set; }

    /// <summary>Amount authorized — equals the order total to the cent.</summary>
    public decimal AuthorizedAmount { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- Hold (authorization) ---
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- Capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    /// <summary>Which saved card (if any) funded this payment. Null for one-off cards.</summary>
    public int? PaymentMethodId { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(string currency, decimal authorizedAmount, string payPalOrderId,
        string authorizationId, string authorizationStatus, DateTimeOffset? authorizationExpiresAt,
        int? paymentMethodId)
    {
        Currency = Guard.Against.NullOrEmpty(currency, nameof(currency));
        AuthorizedAmount = Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));
        PayPalOrderId = Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        AuthorizationExpiresAt = authorizationExpiresAt;
        PaymentMethodId = paymentMethodId;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replace the authorization after a stale one was renewed (reauthorized).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.InvalidInput(Status, nameof(Status), s => s == PaymentStatus.Authorized,
            "Only an authorized payment can have its authorization renewed.");
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount,
        decimal payPalFee, decimal netAmount)
    {
        CaptureId = Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureStatus = Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured amount is still available to refund.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    public Refund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void AddRefund(Refund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        Status = TotalRefunded() >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }
}

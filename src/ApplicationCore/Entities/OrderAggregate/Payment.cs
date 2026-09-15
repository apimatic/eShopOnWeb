using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The money side of an <see cref="Order"/>. Owns the state PayPal owns — the ids and current
/// status of the hold (authorization), the capture, and any refunds — so a later request can
/// act on it, not only the one that started it. Part of the Order aggregate.
/// </summary>
public class Payment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.Pending;
    }

    /// <summary>The order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO-4217 currency code the payment is denominated in.</summary>
    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- PayPal-owned state: the hold ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- PayPal-owned state: the capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>The saved card used to pay, when the shopper paid with a vaulted card.</summary>
    public int? PaymentMethodId { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool IsAuthorized => AuthorizationId is not null &&
        (Status == PaymentStatus.Authorized);

    public bool IsCaptured => CaptureId is not null &&
        (Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded);

    /// <summary>Total value already refunded against the capture.</summary>
    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the capture can still be refunded — never more than was captured.</summary>
    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    /// <summary>Records the outcome of authorizing (holding) the money.</summary>
    public void RecordAuthorization(string payPalOrderId, string authorizationId,
        string authorizationStatus, DateTimeOffset? expiresAt, int? paymentMethodId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        PaymentMethodId = paymentMethodId;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>
    /// Replaces the authorization with a renewed one (PayPal reauthorization yields a new id
    /// and restarts the honor period). Used when an authorization has gone stale before fulfilment.
    /// </summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records the capture (taking the money) as PayPal reported it.</summary>
    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    /// <summary>Records that the hold was released without any money moving.</summary>
    public void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>
    /// Adds a refund and advances status. Never allows the total refunded to exceed the
    /// captured amount.
    /// </summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (TotalRefunded + refund.Amount > (CapturedAmount ?? 0m))
        {
            throw new InvalidOperationException(
                "Refund would exceed the captured amount for this payment.");
        }

        _refunds.Add(refund);
        Status = TotalRefunded >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }

    /// <summary>Finds an already-recorded refund for a caller idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

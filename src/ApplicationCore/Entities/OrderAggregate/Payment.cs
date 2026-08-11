using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Holds the money state that PayPal owns for an <see cref="Order"/>: the hold (authorization),
/// the capture and the refunds, together with the ids and current statuses PayPal reported for
/// each, so a later request can act on the payment and not only the one that started it.
///
/// The <see cref="Payment"/> is part of the <see cref="Order"/> aggregate and is only mutated
/// through the order and through the payment orchestration service.
/// </summary>
public class Payment : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(
        string currency,
        decimal authorizedAmount,
        string paypalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt,
        int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        Currency = currency;
        AuthorizedAmount = authorizedAmount;
        PayPalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        SavedPaymentMethodId = savedPaymentMethodId;
        State = PaymentState.Authorized;
    }

    public PaymentState State { get; private set; }

    public string Currency { get; private set; }

    /// <summary>The amount held at authorization; equals the order total to the cent.</summary>
    public decimal AuthorizedAmount { get; private set; }

    // ----- The hold (authorization) -----
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // ----- The capture -----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGrossAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    /// <summary>The saved card this payment was made with, when one was used (Flow 2).</summary>
    public int? SavedPaymentMethodId { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>
    /// Replaces the hold with a freshly created authorization (PayPal reauthorization), used when
    /// the original hold has gone stale before fulfilment.
    /// </summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        if (State != PaymentState.Authorized)
        {
            throw new InvalidOperationException("Only an authorized payment can be reauthorized.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records the capture that took the money at fulfilment.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal grossAmount, decimal paypalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGrossAmount = grossAmount;
        PayPalFeeAmount = paypalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
        State = PaymentState.Captured;
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        State = PaymentState.Voided;
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>The captured amount still available to refund.</summary>
    public decimal RefundableRemaining() => (CapturedGrossAmount ?? 0m) - TotalRefunded();

    /// <summary>
    /// Returns the refund already recorded under <paramref name="idempotencyKey"/>, or null.
    /// </summary>
    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (State != PaymentState.Captured && State != PaymentState.PartiallyRefunded)
        {
            throw new InvalidOperationException("Only a captured payment can be refunded.");
        }

        _refunds.Add(refund);

        var remaining = RefundableRemaining();
        State = remaining <= 0m ? PaymentState.Refunded : PaymentState.PartiallyRefunded;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-owned state of an <see cref="Order"/>'s payment: the ids and current status of the hold, the
/// capture and the refunds, plus the captured amount, PayPal's fee and the net proceeds. This carries enough
/// state that a later request (capture, void, refund) can act on it, not only the one that started it.
/// </summary>
public class OrderPayment : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt, string currency, decimal authorizedAmount,
        string paymentMethodDescription, bool usedSavedCard)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        Currency = currency;
        AuthorizedAmount = authorizedAmount;
        PaymentMethodDescription = paymentMethodDescription;
        UsedSavedCard = usedSavedCard;
        AuthorizedAt = DateTimeOffset.Now;
    }

    // --- Authorization (the hold) ---
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset AuthorizedAt { get; private set; }
    public string Currency { get; private set; }
    public decimal AuthorizedAmount { get; private set; }

    /// <summary>Safe description of the card used (e.g. "Visa ****1111"); never full card details.</summary>
    public string PaymentMethodDescription { get; private set; }
    public bool UsedSavedCard { get; private set; }

    // --- Capture (taken at fulfilment) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGrossAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // --- Refunds ---
    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Replace the authorization after a reauthorization renewed a stale hold.</summary>
    public void RenewAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string status, decimal grossAmount, decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedGrossAmount = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.Now;
    }

    public void AddRefund(PaymentRefund refund) => _refunds.Add(refund);

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>Amount still available to refund against the captured gross.</summary>
    public decimal RefundableRemaining() => (CapturedGrossAmount ?? 0m) - TotalRefunded();

    /// <summary>Find a refund already recorded under the given idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

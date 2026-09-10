using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment that backs an <see cref="Order"/>. Carries the state PayPal owns — the ids and
/// current status of the hold (authorization), the capture and the refunds — so that a later
/// request (fulfil, cancel, refund, reconcile) can act on it without the context of the request
/// that started it. Part of the Order aggregate; never stores raw card details.
/// </summary>
public class Payment
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(string payPalOrderId, string authorizationId, string authorizationStatus,
        decimal authorizedAmount, string currency, DateTimeOffset? authorizationExpiresAt,
        string paymentInstrumentDescription)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        AuthorizationExpiresAt = authorizationExpiresAt;
        PaymentInstrumentDescription = paymentInstrumentDescription;
    }

    /// <summary>The PayPal v2 checkout order id that produced the authorization.</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>Current PayPal authorization id (changes when re-authorized).</summary>
    public string AuthorizationId { get; private set; }

    /// <summary>PayPal authorization status: CREATED, CAPTURED, VOIDED, EXPIRED.</summary>
    public string AuthorizationStatus { get; private set; }

    public decimal AuthorizedAmount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>When the current authorization's honor period expires (best effort from PayPal).</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>Safe description of the instrument used, e.g. "VISA ****1111". Never full card data.</summary>
    public string PaymentInstrumentDescription { get; private set; }

    public string? CaptureId { get; private set; }

    /// <summary>PayPal capture status: COMPLETED, PARTIALLY_REFUNDED, REFUNDED.</summary>
    public string? CaptureStatus { get; private set; }

    /// <summary>Gross amount PayPal captured.</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>Fee PayPal charged on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant (gross - fee).</summary>
    public decimal? NetAmount { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Total already refunded against the capture.</summary>
    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    public void UpdateAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
    }

    public void MarkAuthorizationVoided() => AuthorizationStatus = "VOIDED";

    /// <summary>
    /// Returns the refund already recorded under <paramref name="idempotencyKey"/>, if any, so a
    /// repeated refund request is a no-op.
    /// </summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Amount that can still legitimately be refunded (captured gross minus what is already refunded).
    /// </summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    /// <summary>
    /// Records a refund against the capture. Guards that a partial refund can never exceed what
    /// remains refundable, so the order never becomes refundable beyond what was captured.
    /// </summary>
    public PaymentRefund AddRefund(string refundId, string idempotencyKey, decimal amount, string status)
    {
        var refund = new PaymentRefund(refundId, idempotencyKey, amount, Currency, status);
        _refunds.Add(refund);
        CaptureStatus = TotalRefunded >= (CapturedAmount ?? 0m) ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return refund;
    }
}

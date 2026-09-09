using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-owned state for an order's payment. It records enough of what PayPal
/// owns (ids and current status for the hold, the capture and the refunds) that a
/// later request can act on the payment, not only the one that started it.
/// No card details are ever stored here.
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
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The amount to be held/captured, equal to the order total to the cent.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>PayPal Checkout order id backing this payment.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>PayPal authorization (hold) id.</summary>
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>PayPal capture id, once the money has been taken at fulfilment.</summary>
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }

    /// <summary>What PayPal reported at capture: captured gross, fee, and net proceeds.</summary>
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>The saved card used to pay, when one was named instead of a one-off card.</summary>
    public int? SavedPaymentMethodId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string status,
        DateTimeOffset? expiresAt, int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        SavedPaymentMethodId = savedPaymentMethodId;
        Touch();
    }

    /// <summary>Records a renewed authorization (id and expiry can change on reauthorize).</summary>
    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void RecordCapture(string captureId, string status, decimal? capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Touch();
    }

    public void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
        Touch();
    }

    public PaymentRefund AddRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        var refund = new PaymentRefund(idempotencyKey, payPalRefundId, amount, Currency, status);
        _refunds.Add(refund);
        Touch();
        return refund;
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>The captured amount still available to be refunded.</summary>
    public decimal RefundableRemaining()
    {
        var captured = CapturedAmount ?? 0m;
        return captured - TotalRefunded();
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

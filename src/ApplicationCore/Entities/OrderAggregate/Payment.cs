using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The money side of an <see cref="Order"/>. Part of the Order aggregate. It records the state PayPal
/// owns — the checkout order id, the authorization (hold), the capture, and any refunds — so that a
/// later request (fulfil, cancel, refund) can act on the payment without re-deriving anything.
/// No raw card data ever lives here: only the safe brand/last-four echoed back by PayPal.
/// </summary>
public class Payment
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(string payPalOrderId, string currency, decimal authorizedAmount, string? cardBrand, string? cardLast4)
    {
        PayPalOrderId = payPalOrderId;
        Currency = currency;
        AuthorizedAmount = authorizedAmount;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
    }

    /// <summary>PayPal Checkout order id (v2/checkout/orders).</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>ISO currency code the hold/capture is denominated in.</summary>
    public string Currency { get; private set; }

    /// <summary>Amount authorized (the hold), equal to the order total to the cent.</summary>
    public decimal AuthorizedAmount { get; private set; }

    // Safe card descriptors echoed by PayPal (never the PAN).
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    // --- Authorization (the hold) ---
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- Capture (the money movement) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- Refunds ---
    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void RecordAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>A reauthorization replaces the authorization id with the new one PayPal returns.</summary>
    public void RecordReauthorization(string newAuthorizationId, string status, DateTimeOffset? expiresAt)
    {
        AuthorizationId = newAuthorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
    }

    public void AddRefund(PaymentRefund refund) => _refunds.Add(refund);

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>Sum of refunds that actually consumed captured funds.</summary>
    public decimal TotalRefunded => _refunds.Where(r => r.CountsAgainstCapture).Sum(r => r.Amount);

    /// <summary>How much of the capture is still refundable.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;
}

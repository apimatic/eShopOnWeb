using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-owned state of an order's payment: the ids and current status for the hold
/// (authorization), the capture, and each refund — enough that a later request (fulfil, cancel,
/// refund) can act on it, not only the request that started it. Owned by <see cref="Order"/>.
/// </summary>
public class OrderPayment
{
    private readonly List<PaymentRefund> _refunds = new();

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(string currencyCode, string referenceId, decimal authorizedAmount)
    {
        CurrencyCode = currencyCode;
        ReferenceId = referenceId;
        AuthorizedAmount = authorizedAmount;
    }

    /// <summary>ISO-4217 currency the payment is denominated in (from configuration).</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>
    /// Our own reference echoed into PayPal's <c>invoice_id</c>/<c>custom_id</c> and used to line the
    /// order up against PayPal's transaction record during reconciliation.
    /// </summary>
    public string ReferenceId { get; private set; }

    /// <summary>The amount held at authorization — equals the order total to the cent.</summary>
    public decimal AuthorizedAmount { get; private set; }

    // --- PayPal-owned ids / status ---

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }

    /// <summary>PayPal's fee reported at capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant reported at capture.</summary>
    public decimal? NetAmount { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    public void RecordPayPalOrder(string payPalOrderId) => PayPalOrderId = payPalOrderId;

    public void RecordAuthorization(string authorizationId, string? status, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string? status, decimal? capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
    }

    public void MarkVoided() => AuthorizationStatus = "VOIDED";

    public PaymentRefund AddRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        var refund = new PaymentRefund(idempotencyKey, payPalRefundId, amount, status);
        _refunds.Add(refund);
        return refund;
    }

    /// <summary>The refund previously recorded under this idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>Amount that can still be refunded without exceeding what was captured.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;
}

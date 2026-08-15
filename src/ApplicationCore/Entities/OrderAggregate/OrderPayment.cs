using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Holds the payment state that PayPal owns for an order — the ids and current status of the
/// hold (authorization), the capture and the refunds — so that a later request (fulfil, cancel,
/// refund) can act on it, not only the request that started the payment. Card numbers are never
/// stored here; only the safe display fields (brand, last four) PayPal returns.
/// </summary>
public class OrderPayment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(string currency, decimal amount, string invoiceId)
    {
        Currency = currency;
        AuthorizedAmount = amount;
        InvoiceId = invoiceId;
    }

    /// <summary>ISO-4217 currency the payment is denominated in (from configuration).</summary>
    public string Currency { get; private set; }

    /// <summary>The eShop-owned reference sent to PayPal as the purchase unit invoice id; used to line
    /// PayPal transactions back up against eShop orders during reconciliation.</summary>
    public string InvoiceId { get; private set; }

    // ---- Hold (authorization) ----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>The amount held. Must equal the order total to the cent.</summary>
    public decimal AuthorizedAmount { get; private set; }

    // ---- Safe card display / vault reference ----
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }
    public string? VaultTokenIdUsed { get; private set; }

    // ---- Capture (money taken at fulfilment) ----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void SetPayPalOrderId(string payPalOrderId) => PayPalOrderId = payPalOrderId;

    public void SetAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt,
        string? cardBrand, string? cardLast4, string? vaultTokenIdUsed)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand ?? CardBrand;
        CardLast4 = cardLast4 ?? CardLast4;
        VaultTokenIdUsed = vaultTokenIdUsed ?? VaultTokenIdUsed;
    }

    public void SetCapture(string captureId, string status, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public void MarkVoided() => AuthorizationStatus = "VOIDED";

    public bool IsAuthorized => !string.IsNullOrEmpty(AuthorizationId);
    public bool IsCaptured => !string.IsNullOrEmpty(CaptureId);

    public decimal TotalRefunded() => _refunds
        .Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        .Sum(r => r.Amount);

    /// <summary>The amount still refundable — never more than what was captured.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public PaymentRefund AddRefund(string idempotencyKey, decimal amount)
    {
        var refund = new PaymentRefund(idempotencyKey, amount);
        _refunds.Add(refund);
        return refund;
    }
}

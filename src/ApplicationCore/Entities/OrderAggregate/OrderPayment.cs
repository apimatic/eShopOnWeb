using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment facet of an <see cref="Order"/>. Holds the state that PayPal owns — the ids and
/// current status of the hold (authorization), the capture, and every refund — so that a later
/// request can act on the payment, not only the one that started it.
/// </summary>
public class OrderPayment
{
    private readonly List<PaymentRefund> _refunds = new();

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        Amount = amount;
        Currency = currency;
        // Stable idempotency keys so a double-clicked authorize/capture never charges twice.
        AuthorizeIdempotencyKey = Guid.NewGuid().ToString("N");
        CaptureIdempotencyKey = Guid.NewGuid().ToString("N");
    }

    /// <summary>The amount to hold/capture — the order total, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO-4217 currency code, taken from configuration.</summary>
    public string Currency { get; private set; }

    /// <summary>
    /// The merchant reference sent to PayPal as invoice_id/custom_id. Lets the reconciliation
    /// report line a PayPal transaction back up against this eShop order.
    /// </summary>
    public string? MerchantReference { get; private set; }

    // --- The hold (authorization) ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- The capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGross { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- How it was paid (safe descriptors only; never full card details) ---
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }
    public string? SavedPaymentMethodDescriptor { get; private set; }

    // --- Idempotency keys reused as PayPal-Request-Id across retries ---
    public string AuthorizeIdempotencyKey { get; private set; }
    public string CaptureIdempotencyKey { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool IsAuthorized => !string.IsNullOrEmpty(AuthorizationId);
    public bool IsCaptured => !string.IsNullOrEmpty(CaptureId);

    /// <summary>The total refunded so far, in the order currency.</summary>
    public decimal TotalRefunded => _refunds
        .Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        .Sum(r => r.Amount);

    /// <summary>How much of the captured amount could still be refunded.</summary>
    public decimal RefundableRemaining => (CapturedGross ?? 0m) - TotalRefunded;

    public void AssignMerchantReference(string reference)
    {
        if (string.IsNullOrEmpty(MerchantReference))
        {
            MerchantReference = reference;
        }
    }

    public void RecordPayPalOrder(string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
    }

    public void RecordAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt,
        string? cardBrand, string? cardLast4, string? savedMethodDescriptor)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        SavedPaymentMethodDescriptor = savedMethodDescriptor;
    }

    public void RefreshAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkAuthorizationVoided()
    {
        AuthorizationStatus = "VOIDED";
    }

    public void RecordCapture(string captureId, string status, decimal gross, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedGross = gross;
        PayPalFee = fee;
        NetAmount = net;
    }

    public PaymentRefund AddRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        var refund = new PaymentRefund(idempotencyKey, payPalRefundId, amount, status);
        _refunds.Add(refund);
        return refund;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>True when the hold has passed its expiry and must be renewed before capture.</summary>
    public bool IsAuthorizationExpired(DateTimeOffset asOf) =>
        AuthorizationExpiresAt.HasValue && asOf >= AuthorizationExpiresAt.Value;
}

using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-owned state for an order's payment. It carries enough of what PayPal owns — the ids
/// and current status of the hold (authorization), the capture, and any refunds — that a later
/// request can act on it, not only the one that started it. It is part of the Order aggregate.
/// </summary>
public class Payment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(string payPalOrderId, string currencyCode, decimal authorizedAmount,
        string? instrumentDescription, string invoiceId)
    {
        PayPalOrderId = Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        CurrencyCode = Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        AuthorizedAmount = authorizedAmount;
        InstrumentDescription = instrumentDescription;
        InvoiceId = Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
    }

    /// <summary>PayPal v2 checkout order id created for this payment.</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>
    /// The invoice id sent to PayPal (unique per payment). Set on the order, capture and refunds,
    /// and read back from Transaction Search to reconcile this payment against its PayPal record.
    /// </summary>
    public string InvoiceId { get; private set; }

    /// <summary>ISO-4217 currency code the payment is denominated in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>Amount authorized (held). Equals the order total to the cent.</summary>
    public decimal AuthorizedAmount { get; private set; }

    /// <summary>Safe description of the funding instrument, e.g. "Visa ending 1111".</summary>
    public string? InstrumentDescription { get; private set; }

    // --- Authorization (the hold) ---
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }

    // --- Capture (the money actually taken) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGrossAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public void SetAuthorization(string authorizationId, string status)
    {
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = status;
    }

    /// <summary>Records a replacement authorization created by re-authorizing a stale hold.</summary>
    public void ReplaceAuthorization(string authorizationId, string status)
    {
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = status;
    }

    public void UpdateAuthorizationStatus(string status) => AuthorizationStatus = status;

    public void SetCapture(string captureId, string status, decimal grossAmount, decimal payPalFee, decimal netAmount)
    {
        CaptureId = Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureStatus = status;
        CapturedGrossAmount = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateCaptureStatus(string status) => CaptureStatus = status;

    public bool IsAuthorized => !string.IsNullOrEmpty(AuthorizationId);
    public bool IsCaptured => !string.IsNullOrEmpty(CaptureId);

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining() => (CapturedGrossAmount ?? AuthorizedAmount) - TotalRefunded();

    public Refund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public Refund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new Refund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }
}

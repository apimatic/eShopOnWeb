using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The money-movement record for an <see cref="Order"/>. It carries enough of the state PayPal owns
/// — the ids and current status of the hold (authorization), the capture, and each refund — that a
/// later request (fulfil / cancel / refund) can act on it, not only the request that started it.
/// No full card details are ever stored here.
/// </summary>
public class Payment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, decimal amount, string currency, string reconciliationId)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(reconciliationId, nameof(reconciliationId));

        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        ReconciliationId = reconciliationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>Currency (ISO-4217) of the payment, from configuration.</summary>
    public string Currency { get; private set; }

    /// <summary>The authorized amount — equals the order total to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// Stable merchant reference sent to PayPal as both <c>custom_id</c> and <c>invoice_id</c>.
    /// Used to line PayPal transactions back up against this order during reconciliation.
    /// </summary>
    public string ReconciliationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // ---- Hold (PayPal Orders v2 + authorization) ----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // ---- Capture (taken at fulfilment) ----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // ---- Instrument used (safe display only) ----
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }
    /// <summary>The saved card used to pay, if the shopper paid with one of their vaulted cards.</summary>
    public int? SavedCardId { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? cardBrand, string? cardLast4, int? savedCardId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        SavedCardId = savedCardId;
    }

    /// <summary>A stale hold was renewed with a new PayPal authorization id.</summary>
    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid() => AuthorizationStatus = "VOIDED";

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
    }

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>Sum of all refunds already issued against the capture.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured payment can still be refunded.</summary>
    public decimal RemainingRefundable() => (CapturedAmount ?? 0m) - TotalRefunded();

    public bool IsCaptured => CaptureId != null;
}

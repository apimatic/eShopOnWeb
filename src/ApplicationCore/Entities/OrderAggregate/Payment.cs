using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment for an <see cref="Order"/>. Owned by the order aggregate, it carries enough of
/// the state that PayPal owns — the ids and current status of the hold (authorization), the
/// capture, and any refunds — that a later request can act on it, not only the one that
/// created it. Full card details are never stored here.
/// </summary>
public class Payment
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(string currency, decimal authorizedAmount, string invoiceId,
        string authorizeRequestId, string captureRequestId)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));

        Currency = currency;
        AuthorizedAmount = authorizedAmount;
        InvoiceId = invoiceId;
        AuthorizeRequestId = authorizeRequestId;
        CaptureRequestId = captureRequestId;
        Status = PaymentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The three-character ISO-4217 currency code used for every PayPal call.</summary>
    public string Currency { get; private set; }

    /// <summary>The amount held/authorized — equal to the order total to the cent.</summary>
    public decimal AuthorizedAmount { get; private set; }

    /// <summary>Merchant reference (invoice id) carried into PayPal so the transaction can be reconciled.</summary>
    public string InvoiceId { get; private set; }

    /// <summary>Idempotency key reused across authorize attempts so a double-click never authorizes twice.</summary>
    public string AuthorizeRequestId { get; private set; }

    /// <summary>Idempotency key reused across capture attempts so a double-click never captures twice.</summary>
    public string CaptureRequestId { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // --- State PayPal owns ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // --- Safe description of the instrument that funded the payment (never full card data) ---
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void SetInstrumentDescription(string? brand, string? last4)
    {
        CardBrand = brand;
        CardLast4 = last4;
    }

    public void RecordAuthorization(string payPalOrderId, string authorizationId,
        string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replaces the current authorization with a fresh one after a reauthorization.</summary>
    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>The total already returned to the shopper across all completed/pending refunds.</summary>
    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured payment can still be refunded.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Records a refund. A partly-refunded order never becomes refundable beyond what was
    /// captured: the guard rejects any amount exceeding the remaining refundable balance.
    /// </summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException("Only a captured payment can be refunded.");
        }
        if (refund.Amount > RefundableRemaining)
        {
            throw new InvalidOperationException(
                $"Refund amount {refund.Amount} exceeds the refundable remaining balance {RefundableRemaining}.");
        }

        _refunds.Add(refund);
        Status = RefundableRemaining <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }
}

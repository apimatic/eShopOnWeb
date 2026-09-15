using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-owned state for an order's payment. Owned by the <see cref="Order"/> aggregate root.
/// It carries enough of the state PayPal owns — the ids and current status for the hold (authorization),
/// the capture, and the refunds — that a later request can act on it, not only the one that started it.
/// No card numbers are ever stored here; only a safe human-readable description of the instrument used.
/// </summary>
public class OrderPayment
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(string currency, decimal amount)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Currency = currency;
        Amount = amount;
        Nonce = Guid.NewGuid().ToString("N");
    }

    /// <summary>The three-letter ISO currency code the payment is denominated in.</summary>
    public string Currency { get; private set; }

    /// <summary>
    /// A stable per-payment nonce. It seeds the PayPal idempotency keys (PayPal-Request-Id) for the create-order
    /// and authorize calls, which happen before any PayPal id exists — so a retried /pay reuses the same keys
    /// (PayPal de-duplicates), while a fresh payment gets globally unique keys that never collide across runs.
    /// </summary>
    public string Nonce { get; private set; }

    /// <summary>The order total that is authorized/captured, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>A safe description of the instrument used, e.g. "Visa ending 1111". Never full card details.</summary>
    public string? InstrumentDescription { get; private set; }

    /// <summary>
    /// The exact invoice id sent to PayPal (globally unique per PayPal order). It is what reconciliation
    /// matches PayPal's transaction records back to this eShop order by.
    /// </summary>
    public string? InvoiceReference { get; private set; }

    // --- Hold (authorization) ---------------------------------------------------------------
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- Capture ----------------------------------------------------------------------------
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGross { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- Refunds ----------------------------------------------------------------------------
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>The total amount refunded so far via refunds that have not failed.</summary>
    public decimal RefundedAmount => _refunds
        .Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        .Sum(r => r.Amount);

    public bool HasAuthorization => !string.IsNullOrEmpty(AuthorizationId);
    public bool HasCapture => !string.IsNullOrEmpty(CaptureId);

    public void RecordPayPalOrder(string payPalOrderId, string invoiceReference)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
        InvoiceReference = invoiceReference;
    }

    public void RecordAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt, string? instrumentDescription)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        if (!string.IsNullOrEmpty(instrumentDescription))
        {
            InstrumentDescription = instrumentDescription;
        }
    }

    /// <summary>
    /// Replaces the hold with a fresh authorization (used when a stale authorization is renewed at fulfilment).
    /// </summary>
    public void RenewAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string status, decimal capturedGross, decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedGross = capturedGross;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
    }

    public void MarkAuthorizationVoided()
    {
        AuthorizationStatus = "VOIDED";
    }

    /// <summary>
    /// The amount still eligible to be refunded: captured gross minus what has already been refunded.
    /// </summary>
    public decimal RefundableRemaining => (CapturedGross ?? 0m) - RefundedAmount;

    /// <summary>
    /// Records a refund against the capture. Guards that the running refunded total never exceeds
    /// the captured amount, so a partly-refunded order never becomes refundable beyond what was captured.
    /// </summary>
    public PaymentRefund AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        if (!HasCapture)
        {
            throw new InvalidOperationException("Cannot refund a payment that has not been captured.");
        }

        if (amount <= 0m)
        {
            throw new InvalidOperationException("Refund amount must be a positive number.");
        }

        if (amount - RefundableRemaining > 0.0001m)
        {
            throw new InvalidOperationException(
                $"Refund of {amount:0.00} exceeds the refundable remaining of {RefundableRemaining:0.00} {Currency}.");
        }

        var refund = new PaymentRefund(refundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
        => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment for an <see cref="Order"/>. It carries enough of the state the processor (PayPal)
/// owns — the ids and current status of the hold (authorization), the capture and the refunds —
/// that a later request can act on it, not just the request that started it.
///
/// Modelled as an entity owned by the Order aggregate. No full card details are ever stored here.
/// </summary>
public class OrderPayment // Owned entity of the Order aggregate
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(string provider, string currency, decimal amount, string paymentReference, string paymentMethodDescription)
    {
        Guard.Against.NullOrEmpty(provider, nameof(provider));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(paymentReference, nameof(paymentReference));

        Provider = provider;
        Currency = currency;
        Amount = amount;
        PaymentReference = paymentReference;
        PaymentMethodDescription = paymentMethodDescription;
        Status = PaymentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The payment processor, always "PayPal" for this integration.</summary>
    public string Provider { get; private set; }

    /// <summary>The ISO-4217 currency the payment is denominated in (from configuration).</summary>
    public string Currency { get; private set; }

    /// <summary>The order total that is authorized/captured, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// A stable, unique reference we mint per payment and pass to PayPal as the invoice/custom id.
    /// Used to line PayPal's transaction records back up against eShop orders during reconciliation.
    /// </summary>
    public string PaymentReference { get; private set; }

    /// <summary>A safe, human description of the funding source (e.g. "Visa ending 1111"). Never the PAN.</summary>
    public string PaymentMethodDescription { get; private set; }

    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // --- Hold (authorization) ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- Capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Total value refunded so far against the capture (counting non-failed refunds).</summary>
    public decimal TotalRefunded => _refunds
        .Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        .Sum(r => r.Amount);

    /// <summary>How much of the capture may still be refunded.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replace the hold with a freshly renewed authorization (reauthorize before capture).</summary>
    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>
    /// Register a refund against the capture. Guards that the running total can never exceed what was
    /// captured, so a partly-refunded order never becomes refundable beyond the captured amount.
    /// </summary>
    public PaymentRefund AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new OrderPaymentException("Only a captured payment can be refunded.");
        }

        Guard.Against.NegativeOrZero(amount, nameof(amount));

        if (amount > RefundableRemaining + 0.0001m)
        {
            throw new OrderPaymentException(
                $"Refund of {amount:0.00} {Currency} exceeds the refundable remaining of {RefundableRemaining:0.00} {Currency}.");
        }

        var refund = new PaymentRefund(refundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = TotalRefunded >= (CapturedAmount ?? 0m) - 0.0001m
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }

    /// <summary>Find an existing refund created under the supplied idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

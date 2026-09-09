using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment that funds an <see cref="Order"/>. Owned by the order aggregate.
/// It carries the PayPal-owned state (the ids and current status for the hold, the capture and the
/// refunds) so a later request can act on the payment, not only the one that started it.
/// </summary>
public class Payment
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(
        string payPalOrderId,
        string invoiceId,
        decimal amount,
        string currencyCode,
        PaymentSourceType sourceType,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt,
        string? cardBrand,
        string? cardLast4)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        PayPalOrderId = payPalOrderId;
        InvoiceId = invoiceId;
        Amount = amount;
        CurrencyCode = currencyCode;
        SourceType = sourceType;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>The PayPal-generated order id used to create the hold.</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>The merchant invoice id sent to PayPal; used to reconcile against PayPal's records.</summary>
    public string InvoiceId { get; private set; }

    /// <summary>The authorized amount (equals the order total, to the cent).</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    public PaymentSourceType SourceType { get; private set; }

    public string? CardBrand { get; private set; }

    public string? CardLast4 { get; private set; }

    public PaymentStatus Status { get; private set; }

    // ----- The hold (authorization) -----
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // ----- The capture -----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // ----- The refunds -----
    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>
    /// Replace the authorization with a freshly renewed one (used when the original hold has gone
    /// stale before fulfilment and had to be reauthorized).
    /// </summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Cannot renew the authorization for a payment in state '{Status}'.");
        }
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records the capture taken at fulfilment, including PayPal's fee and the net proceeds.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Only an authorized payment can be captured; this payment is '{Status}'.");
        }
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    /// <summary>Records that the hold was released (voided) before any capture.</summary>
    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Only an authorized payment can be cancelled; this payment is '{Status}'.");
        }
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>The amount still available to refund: captured minus what has already been refunded.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>Returns an already-recorded refund for the given idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Validates that a refund of <paramref name="amount"/> is legitimate before it is issued:
    /// the payment must be captured and the amount must not exceed what remains refundable.
    /// </summary>
    public void EnsureRefundable(decimal amount)
    {
        if (Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentStateException($"Only a captured payment can be refunded; this payment is '{Status}'.");
        }
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (amount > RefundableRemaining())
        {
            throw new PaymentStateException(
                $"Refund of {amount:0.00} exceeds the refundable remaining amount of {RefundableRemaining():0.00}.");
        }
    }

    /// <summary>Records a completed refund and advances the status to partially/fully refunded.</summary>
    public void AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        EnsureRefundable(amount);
        _refunds.Add(new PaymentRefund(refundId, amount, status, idempotencyKey));
        Status = TotalRefunded() >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }
}

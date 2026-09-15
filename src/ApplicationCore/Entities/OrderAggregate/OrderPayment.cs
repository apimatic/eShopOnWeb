using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-backed payment for an <see cref="Order"/>. Carries the identifiers and
/// current status that PayPal owns for the hold (authorization), the capture, and any
/// refunds, so a later request can act on the payment, not only the one that started it.
/// Part of the Order aggregate and only mutated through its behavior methods.
/// Modelled as an owned type (no identity of its own — it lives with its order).
/// </summary>
public class OrderPayment
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(
        string currencyCode,
        decimal amount,
        string payPalOrderId,
        string invoiceId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt,
        int? savedPaymentMethodId,
        string cardDescriptor)
    {
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        CurrencyCode = currencyCode;
        Amount = amount;
        PayPalOrderId = payPalOrderId;
        InvoiceId = invoiceId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        SavedPaymentMethodId = savedPaymentMethodId;
        CardDescriptor = cardDescriptor;
        Status = PaymentStatus.Authorized;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public string Provider { get; private set; } = "PayPal";
    public string CurrencyCode { get; private set; }

    /// <summary>The amount held / to be captured — equal to the order total to the cent.</summary>
    public decimal Amount { get; private set; }

    // --- Identifiers PayPal owns ---
    public string PayPalOrderId { get; private set; }
    public string InvoiceId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }

    // --- Amounts PayPal reported at capture ---
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>Set when the shopper paid with one of their saved cards.</summary>
    public int? SavedPaymentMethodId { get; private set; }

    /// <summary>A safe, human-recognisable label for the funding card (never full details).</summary>
    public string? CardDescriptor { get; private set; }

    public PaymentStatus Status { get; private set; }
    public DateTimeOffset AuthorizedAt { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the capture is still available to refund.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    /// <summary>Replace the authorization with a renewed one (after the honor period lapses).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Only an authorized payment can be renewed; current status is {Status}.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Record the capture reported by PayPal at fulfilment.</summary>
    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Only an authorized payment can be captured; current status is {Status}.");
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    /// <summary>Record that the hold was released before fulfilment.</summary>
    public void RecordVoid()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Only an authorized (uncaptured) payment can be cancelled; current status is {Status}.");
        }

        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>Add a refund against the capture, keeping the running total within the captured amount.</summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentStateException($"Only a captured payment can be refunded; current status is {Status}.");
        }
        if (refund.Amount > RefundableRemaining)
        {
            throw new PaymentStateException(
                $"Refund of {refund.Amount:0.00} exceeds the refundable remaining {RefundableRemaining:0.00} {CurrencyCode}.");
        }

        _refunds.Add(refund);
        Status = TotalRefunded >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
        => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

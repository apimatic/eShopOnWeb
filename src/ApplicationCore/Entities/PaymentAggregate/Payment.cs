using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Holds the money-movement state for a single <see cref="OrderAggregate.Order"/>: the ids and
/// current status the payment processor owns for the hold (authorization), the capture and the
/// refunds. Kept as its own aggregate so the reusable Order/OrderItem model stays payment-agnostic,
/// while carrying enough processor state that a later request can act on it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, string currencyCode, decimal amount, string invoiceReference)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(invoiceReference, nameof(invoiceReference));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
        InvoiceReference = invoiceReference;
        Status = PaymentStatus.Pending;
    }

    /// <summary>
    /// Globally-unique invoice reference sent to PayPal (invoice_id). Used to line PayPal's
    /// transaction record up against this payment unambiguously during reconciliation.
    /// </summary>
    public string InvoiceReference { get; private set; }

    /// <summary>The eShop order this payment belongs to (one payment per order).</summary>
    public int OrderId { get; private set; }

    /// <summary>Owner of the order/payment; used for shopper scoping.</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>The order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    // ---- Processor-owned state for the hold ----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // ---- Processor-owned state for the capture ----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }

    /// <summary>Fee PayPal reported on capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant PayPal reported on capture.</summary>
    public decimal? NetAmount { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Sum of refunds that have not been cancelled/failed by the processor.</summary>
    public decimal TotalRefunded =>
        _refunds.Where(r => !IsDeadRefundStatus(r.Status)).Sum(r => r.Amount);

    /// <summary>How much of the capture is still refundable.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    public bool IsAuthorized => AuthorizationId is not null &&
        (Status == PaymentStatus.Authorized);

    public bool IsCaptured => CaptureId is not null;

    private static bool IsDeadRefundStatus(string status) =>
        string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase);

    public void RecordAuthorization(string payPalOrderId, string authorizationId,
        string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>
    /// Records a renewed authorization (reauthorize) that replaces a stale hold before capture.
    /// </summary>
    public void RecordReauthorization(string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt)
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
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>Existing refund issued under the given idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    /// <summary>
    /// Validates that a refund of <paramref name="amount"/> would not exceed what remains
    /// refundable on the capture.
    /// </summary>
    public void EnsureRefundable(decimal amount)
    {
        if (!IsCaptured)
        {
            throw new PaymentOperationException(
                "Cannot refund a payment that has not been captured.");
        }
        if (amount <= 0m)
        {
            throw new PaymentOperationException("Refund amount must be greater than zero.");
        }
        if (amount > RefundableRemaining)
        {
            throw new PaymentOperationException(
                $"Refund amount {amount} exceeds the refundable remaining {RefundableRemaining} " +
                $"(captured {CapturedAmount}, already refunded {TotalRefunded}).");
        }
    }

    public PaymentRefund AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new PaymentRefund(refundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = RefundableRemaining <= 0m
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }
}

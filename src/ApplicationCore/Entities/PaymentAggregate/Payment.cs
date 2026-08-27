using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Records the PayPal-owned state of the money movement for an order: the authorization
/// (hold), the capture, and any refunds, so later requests can act on them.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currency, string payPalOrderId, string invoiceId, string? paymentMethodLabel)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        PayPalOrderId = payPalOrderId;
        InvoiceId = invoiceId;
        PaymentMethodLabel = paymentMethodLabel;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string PayPalOrderId { get; private set; }

    /// <summary>The invoice id sent to PayPal; unique per payment attempt for the merchant.</summary>
    public string InvoiceId { get; private set; }
    public string? PaymentMethodLabel { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool HasActiveAuthorization =>
        AuthorizationId != null &&
        (AuthorizationStatus == "CREATED" || AuthorizationStatus == "PENDING" || AuthorizationStatus == "PARTIALLY_CAPTURED");

    public bool IsCaptured => CaptureId != null &&
        (CaptureStatus == "COMPLETED" || CaptureStatus == "PARTIALLY_REFUNDED" || CaptureStatus == "REFUNDED");

    public decimal TotalRefunded => _refunds
        .Where(r => r.Status == "COMPLETED" || r.Status == "PENDING")
        .Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    /// <summary>
    /// Points this payment at a fresh PayPal order after a previous authorization attempt
    /// failed or was voided, so the shopper can retry paying the same eShop order.
    /// </summary>
    public void ResetForRetry(string payPalOrderId, string invoiceId, string? paymentMethodLabel)
    {
        PayPalOrderId = payPalOrderId;
        InvoiceId = invoiceId;
        PaymentMethodLabel = paymentMethodLabel;
        AuthorizationId = null;
        AuthorizationStatus = null;
        AuthorizationExpiresAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordAuthorizationStatus(string status)
    {
        AuthorizationStatus = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCaptureStatus(string status)
    {
        CaptureStatus = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey, string? note)
    {
        var refund = new PaymentRefund(Id, payPalRefundId, amount, Currency, status, idempotencyKey, note);
        _refunds.Add(refund);
        UpdatedAt = DateTimeOffset.UtcNow;
        return refund;
    }
}

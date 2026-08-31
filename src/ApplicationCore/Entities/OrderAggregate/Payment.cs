using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Payment state for an order. Carries the identifiers and statuses PayPal owns
/// (authorization hold, capture, refunds) so any later request can act on them.
/// Full card details are never stored here.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, string currency)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        Status = PaymentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }

    /// <summary>Unique invoice id sent to PayPal with the authorization (reconciliation key).</summary>
    public string? InvoiceId { get; private set; }

    // Authorization (hold) state owned by PayPal
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // Capture state reported by PayPal at fulfilment
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>Saved card used to pay, when the shopper paid with one.</summary>
    public int? SavedCardId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void MarkAuthorized(string invoiceId, string payPalOrderId, string authorizationId, string authorizationStatus,
        decimal amount, DateTimeOffset? expiresAt, int? savedCardId)
    {
        InvoiceId = invoiceId;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        AuthorizationExpiresAt = expiresAt;
        SavedCardId = savedCardId;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    public void MarkDeclined(string? status)
    {
        AuthorizationStatus = status ?? AuthorizationStatus;
        Status = PaymentStatus.Declined;
        Touch();
    }

    public void MarkAuthorizationRenewed(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void MarkAuthorizationStatus(string status)
    {
        AuthorizationStatus = status;
        Touch();
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? paypalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void MarkVoided(string? status)
    {
        AuthorizationStatus = status ?? AuthorizationStatus;
        Status = PaymentStatus.Voided;
        Touch();
    }

    public PaymentRefund BeginRefund(decimal amount, string idempotencyKey)
    {
        var refund = new PaymentRefund(amount, idempotencyKey);
        _refunds.Add(refund);
        Touch();
        return refund;
    }

    public void CompleteRefund(PaymentRefund refund, string payPalRefundId, string status)
    {
        refund.Complete(payPalRefundId, status);
        Status = TotalRefunded() >= (CapturedAmount ?? 0m) ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        Touch();
    }

    public void FailRefund(PaymentRefund refund, string status)
    {
        refund.Fail(status);
        Touch();
    }

    public void RemoveRefund(PaymentRefund refund)
    {
        _refunds.Remove(refund);
        Touch();
    }

    public decimal TotalRefunded() => _refunds.Where(r => r.CountsAgainstTotal()).Sum(r => r.Amount);

    public decimal RefundableAmount() => (CapturedAmount ?? 0m) - TotalRefunded();

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

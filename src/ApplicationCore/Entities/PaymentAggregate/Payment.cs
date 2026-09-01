using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Payment state for an Order. Carries the PayPal-owned identifiers and statuses
/// (order, authorization/hold, capture, refunds) so any later request can act on them.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal authorizedAmount, string currency,
        string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt, int? savedPaymentMethodId, string invoiceId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));

        OrderId = orderId;
        BuyerId = buyerId;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        SavedPaymentMethodId = savedPaymentMethodId;
        InvoiceId = invoiceId;
        Status = PaymentStatus.Authorized;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public decimal AuthorizedAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    public string PayPalOrderId { get; private set; } = string.Empty;
    /// <summary>Unique invoice id sent to PayPal; appears in PayPal's transaction reports.</summary>
    public string InvoiceId { get; private set; } = string.Empty;
    public string AuthorizationId { get; private set; } = string.Empty;
    public string AuthorizationStatus { get; private set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public int? SavedPaymentMethodId { get; private set; }

    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds
        .Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
        .Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    public void RenewAuthorization(string newAuthorizationId, string newStatus, DateTimeOffset? newExpiresAt)
    {
        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        AuthorizationId = newAuthorizationId;
        AuthorizationStatus = newStatus;
        AuthorizationExpiresAt = newExpiresAt;
    }

    public void MarkCaptured(string captureId, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        Status = PaymentStatus.Voided;
        AuthorizationStatus = "VOIDED";
    }

    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var refund = new PaymentRefund(Id, payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        if (RefundableAmount <= 0m)
        {
            Status = PaymentStatus.Refunded;
        }
        else if (Status == PaymentStatus.Captured)
        {
            Status = PaymentStatus.PartiallyRefunded;
        }

        return refund;
    }
}

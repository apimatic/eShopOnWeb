using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Payment state owned by PayPal (ids and current status for the hold, the capture and
/// any refunds) so that a later request can act on an order's payment. One row per order;
/// a failed authorization attempt is reset in place so a retry gets fresh PayPal
/// references while a repeated call replays the stored idempotency key.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() {}

    public Payment(int orderId, string invoiceId, string currency, string paymentMethodDescription)
    {
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        InvoiceId = invoiceId;
        Currency = currency;
        PaymentMethodDescription = paymentMethodDescription;
        AuthorizationStatus = "PENDING";
    }

    public int OrderId { get; private set; }
    public string? PayPalOrderId { get; private set; }

    /// <summary>Merchant-unique reference sent to PayPal as invoice_id/custom_id; the reconciliation link.</summary>
    public string InvoiceId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string Currency { get; private set; }
    public string PaymentMethodDescription { get; private set; }

    /// <summary>
    /// Sequence number making every outbound mutating PayPal call's idempotency key unique
    /// per attempt, so a retry after a failure is a fresh operation while a replay of the
    /// same attempt reuses the same key.
    /// </summary>
    public int OperationSequence { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded =>
        _refunds.Where(r => r.Status != PaymentRefundStatus.Failed && r.Status != PaymentRefundStatus.Cancelled)
                .Sum(r => r.Amount);

    public decimal RefundableAmount =>
        CapturedAmount.HasValue ? CapturedAmount.Value - TotalRefunded : 0m;

    public string NextOperationKey(string purpose)
    {
        OperationSequence++;
        return $"eshop-{InvoiceId}-{purpose}-{OperationSequence}";
    }

    public void ResetForRetry(string invoiceId, string paymentMethodDescription)
    {
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        InvoiceId = invoiceId;
        PaymentMethodDescription = paymentMethodDescription;
        PayPalOrderId = null;
        AuthorizationId = null;
        AuthorizationStatus = "PENDING";
        AuthorizationExpiresAt = null;
    }

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkFailed()
    {
        AuthorizationStatus = "FAILED";
    }

    public void UpdateAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal grossAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
    }

    public void MarkVoided(string status)
    {
        AuthorizationStatus = status;
    }

    public PaymentRefund AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var refund = new PaymentRefund(Id, refundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }
}

public static class PaymentRefundStatus
{
    public const string Completed = "COMPLETED";
    public const string Pending = "PENDING";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";
}

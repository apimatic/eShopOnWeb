using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment record for an order: PayPal ids and current status for the
/// authorization (funds hold), the capture and any refunds, so later requests
/// can act on a payment that was started earlier.
/// </summary>
public class OrderPayment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string currency, decimal authorizedAmount)
    {
        OrderId = orderId;
        Currency = currency;
        AuthorizedAmount = authorizedAmount;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; }
    public decimal AuthorizedAmount { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int? PaymentMethodId { get; private set; }
    public DateTimeOffset AuthorizedAt { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Amount already refunded or with a refund in flight.</summary>
    public decimal RefundedAmount =>
        _refunds.Where(r => r.Status != PaymentRefund.FailedStatus && r.Status != PaymentRefund.CancelledStatus)
            .Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - RefundedAmount;

    /// <summary>
    /// Records the outcome of an authorization. A denied/voided authorization may be replaced
    /// by a later retry; an active one must not be silently replaced.
    /// </summary>
    public void RecordAuthorization(string? paypalOrderId, string authorizationId, string status,
        DateTimeOffset? expiresAt, decimal amount, int? paymentMethodId)
    {
        PayPalOrderId = paypalOrderId ?? PayPalOrderId;
        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAmount = amount;
        PaymentMethodId = paymentMethodId;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records the outcome of re-authorizing the same hold (id may or may not change).</summary>
    public void RecordRenewal(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string status, decimal amount, decimal? paypalFee, decimal? netAmount)
    {
        PayPalCaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
    }

    public void AddRefund(PaymentRefund refund)
    {
        _refunds.Add(refund);
    }
}

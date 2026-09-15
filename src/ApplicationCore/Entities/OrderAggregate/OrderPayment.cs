using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string currency, decimal authorizedAmount)
    {
        OrderId = orderId;
        Currency = currency;
        AuthorizedAmount = authorizedAmount;
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; }
    public decimal AuthorizedAmount { get; private set; }

    public string? PaypalOrderId { get; private set; }
    public string? PaypalOrderStatus { get; private set; }
    public string? PaypalAuthorizationId { get; private set; }
    public string? PaypalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public DateTimeOffset? AuthorizationCreated { get; private set; }

    public string? PaypalCaptureId { get; private set; }
    public string? PaypalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public decimal RefundedAmount { get; private set; }

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal RemainingRefundable =>
        Math.Max(0, (CapturedAmount ?? 0m) - RefundedAmount);

    public void RecordAuthorization(
        string paypalOrderId,
        string paypalOrderStatus,
        string authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        string currency,
        DateTimeOffset? expiration,
        DateTimeOffset? created)
    {
        PaypalOrderId = paypalOrderId;
        PaypalOrderStatus = paypalOrderStatus;
        PaypalAuthorizationId = authorizationId;
        PaypalAuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        AuthorizationExpiration = expiration;
        AuthorizationCreated = created;
    }

    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiration, DateTimeOffset? created)
    {
        PaypalAuthorizationId = authorizationId;
        PaypalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        AuthorizationCreated = created;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        string currency)
    {
        PaypalCaptureId = captureId;
        PaypalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        Currency = currency;
        PaypalAuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid(string authorizationStatus)
    {
        PaypalAuthorizationStatus = authorizationStatus;
    }

    public OrderRefund AddRefund(string paypalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        var refund = new OrderRefund(paypalRefundId, status, amount, currency, idempotencyKey);
        _refunds.Add(refund);
        RefundedAmount += amount;
        if (!string.IsNullOrEmpty(PaypalCaptureStatus))
        {
            PaypalCaptureStatus = RemainingRefundable <= 0 ? "REFUNDED" : "PARTIALLY_REFUNDED";
        }
        return refund;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        foreach (var refund in _refunds)
        {
            if (string.Equals(refund.IdempotencyKey, idempotencyKey, StringComparison.Ordinal))
            {
                return refund;
            }
        }
        return null;
    }
}

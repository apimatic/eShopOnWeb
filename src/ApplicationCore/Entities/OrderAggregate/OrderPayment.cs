using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, decimal authorizedAmount, string currencyCode)
    {
        OrderId = orderId;
        AuthorizedAmount = authorizedAmount;
        CurrencyCode = currencyCode;
        Status = OrderPaymentStatus.AwaitingAuthorization;
    }

    public int OrderId { get; private set; }
    public OrderPaymentStatus Status { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string CurrencyCode { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefundedAmount => _refunds.Sum(r => r.Amount);

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt, int? savedPaymentMethodId)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = OrderPaymentStatus.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal? feeAmount, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFeeAmount = feeAmount;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = OrderPaymentStatus.Captured;
    }

    public void RecordVoid()
    {
        Status = OrderPaymentStatus.Voided;
    }

    public OrderRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        var existing = _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (CapturedAmount is null)
        {
            throw new OrderPaymentStateException("Cannot refund a payment before it has been captured.");
        }

        if (TotalRefundedAmount + amount > CapturedAmount.Value)
        {
            throw new OrderPaymentStateException(
                $"Refund of {amount} would exceed the captured amount of {CapturedAmount.Value} (already refunded {TotalRefundedAmount}).");
        }

        var refund = new OrderRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        Status = TotalRefundedAmount >= CapturedAmount.Value
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
        return refund;
    }
}

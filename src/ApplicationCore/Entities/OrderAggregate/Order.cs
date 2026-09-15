using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Order() {}

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Status = OrderState.AwaitingPayment;
        PaymentIdempotencyKey = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderState Status { get; private set; } = OrderState.AwaitingPayment;
    public string PaymentIdempotencyKey { get; private set; } = Guid.NewGuid().ToString("N");

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? OriginalAuthorizationTime { get; private set; }
    public DateTimeOffset? AuthorizationTime { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public decimal RefundedTotal() => _refunds.Sum(r => r.Amount);

    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        DateTimeOffset? createTime,
        DateTimeOffset? expirationTime)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        if (Status is OrderState.Fulfilled or OrderState.Cancelled or OrderState.Refunded or OrderState.PartiallyRefunded)
        {
            throw new CheckoutException(409, $"Order {Id} cannot be authorized in state {Status}.");
        }

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        AuthorizationTime = createTime;
        OriginalAuthorizationTime ??= createTime;
        AuthorizationExpirationTime = expirationTime;
        Status = OrderState.Authorized;
    }

    public void ReplaceAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? createTime, DateTimeOffset? expirationTime)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != OrderState.Authorized)
        {
            throw new CheckoutException(409, $"Order {Id} cannot be reauthorized in state {Status}.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationTime = createTime;
        AuthorizationExpirationTime = expirationTime;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status is OrderState.Cancelled or OrderState.AwaitingPayment)
        {
            throw new CheckoutException(409, $"Order {Id} cannot be fulfilled in state {Status}.");
        }

        if (Status is OrderState.Fulfilled or OrderState.Refunded or OrderState.PartiallyRefunded)
        {
            return;
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = OrderState.Fulfilled;
    }

    public void RecordVoid(string? authorizationStatus = "VOIDED")
    {
        if (Status == OrderState.Cancelled)
        {
            return;
        }

        if (Status != OrderState.Authorized && Status != OrderState.AwaitingPayment)
        {
            throw new CheckoutException(409, $"Order {Id} cannot be cancelled in state {Status}.");
        }

        AuthorizationStatus = authorizationStatus;
        Status = OrderState.Cancelled;
    }

    public OrderRefund RecordRefund(string payPalRefundId, string status, decimal amount, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
        if (existing != null)
        {
            return existing;
        }

        if (Status is not (OrderState.Fulfilled or OrderState.PartiallyRefunded or OrderState.Refunded))
        {
            throw new CheckoutException(409, $"Order {Id} cannot be refunded in state {Status}.");
        }

        if (amount <= 0)
        {
            throw new CheckoutException(400, "Refund amount must be greater than zero.");
        }

        if (amount - RemainingRefundable() > 0.0000001m)
        {
            throw new CheckoutException(409,
                $"Refund of {amount:0.00} exceeds remaining refundable amount {RemainingRefundable():0.00}.");
        }

        var refund = new OrderRefund(payPalRefundId, status, amount, idempotencyKey);
        _refunds.Add(refund);

        Status = RemainingRefundable() <= 0.0000001m ? OrderState.Refunded : OrderState.PartiallyRefunded;
        CaptureStatus = Status == OrderState.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return refund;
    }
}

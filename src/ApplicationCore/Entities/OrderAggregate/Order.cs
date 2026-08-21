using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Order() {}
    #pragma warning restore CS8618

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx 
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal? GrossAmount { get; private set; }
    public string? Currency { get; private set; }

    private readonly List<OrderRefund> _refunds = new();
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

    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var refunded = _refunds.Where(r => r.CountsAgainstCaptured).Sum(r => r.Amount);
        return captured - refunded;
    }

    public bool HasActiveAuthorization() =>
        !string.IsNullOrEmpty(AuthorizationId)
        && PaymentStatus is OrderPaymentStatus.Authorized;

    public bool IsAuthorizationStale(DateTimeOffset utcNow) =>
        AuthorizationExpiration.HasValue && utcNow >= AuthorizationExpiration.Value;

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public void AttachPayPalOrder(string payPalOrderId, string? orderStatus, string currency)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = orderStatus;
        Currency = currency;
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string? orderStatus,
        string authorizationId,
        string? authorizationStatus,
        DateTimeOffset? expiration,
        string currency)
    {
        if (PaymentStatus is OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException("This order has already been fulfilled.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = orderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        Currency = currency;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void ReplaceAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiration)
    {
        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new InvalidOperationException("Only an authorized hold can be renewed.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
    }

    public void RecordCapture(
        string captureId,
        string? captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        decimal? grossAmount)
    {
        if (PaymentStatus is OrderPaymentStatus.Cancelled)
        {
            throw new InvalidOperationException("A cancelled order cannot be fulfilled.");
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        GrossAmount = grossAmount;
        AuthorizationStatus = "CAPTURED";
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void RecordVoid()
    {
        if (PaymentStatus is OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException("A fulfilled order cannot be cancelled. Refund it instead.");
        }

        AuthorizationStatus = "VOIDED";
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string payPalRefundId, string status, decimal amount, string idempotencyKey)
    {
        if (PaymentStatus is not OrderPaymentStatus.Fulfilled
            and not OrderPaymentStatus.PartiallyRefunded
            and not OrderPaymentStatus.Refunded)
        {
            throw new InvalidOperationException("Refunds are only allowed after fulfilment.");
        }

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (amount <= 0)
        {
            throw new InvalidOperationException("Refund amount must be greater than zero.");
        }

        var remaining = RemainingRefundable();
        if (amount > remaining)
        {
            throw new InvalidOperationException(
                $"Refund of {amount} exceeds the remaining refundable amount of {remaining}.");
        }

        var refund = new OrderRefund(payPalRefundId, status, amount, idempotencyKey);
        _refunds.Add(refund);

        var leftover = RemainingRefundable();
        PaymentStatus = leftover <= 0m
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
        CaptureStatus = PaymentStatus == OrderPaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";

        return refund;
    }
}

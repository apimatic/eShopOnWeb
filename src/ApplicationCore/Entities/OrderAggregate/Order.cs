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

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        FulfillmentStatus = OrderFulfillmentStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderFulfillmentStatus FulfillmentStatus { get; private set; } = OrderFulfillmentStatus.AwaitingPayment;

    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? PayPalAuthorizationExpiration { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? PaymentCurrency { get; private set; }

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx 
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
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
        if (CapturedAmount is null)
        {
            return 0m;
        }

        var refunded = _refunds.Where(r => r.CountsAgainstCapturedAmount).Sum(r => r.Amount);
        var remaining = CapturedAmount.Value - refunded;
        return remaining < 0m ? 0m : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public void RecordPayPalOrder(string payPalOrderId, string? payPalOrderStatus)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
    }

    public void RecordAuthorization(
        string authorizationId,
        string? authorizationStatus,
        DateTimeOffset? expiration,
        string? payPalOrderStatus,
        string currency)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        PayPalAuthorizationExpiration = expiration;
        PayPalOrderStatus = payPalOrderStatus ?? PayPalOrderStatus;
        PaymentCurrency = currency;
        FulfillmentStatus = OrderFulfillmentStatus.Authorized;
    }

    public void ReplaceAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        PayPalAuthorizationExpiration = expiration;
    }

    public void RecordCapture(
        string captureId,
        string? captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        string? authorizationStatus)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        PayPalAuthorizationStatus = authorizationStatus ?? PayPalAuthorizationStatus;
        FulfillmentStatus = OrderFulfillmentStatus.Fulfilled;
    }

    public void RecordVoid(string? authorizationStatus, string? payPalOrderStatus)
    {
        PayPalAuthorizationStatus = authorizationStatus ?? PayPalAuthorizationStatus;
        PayPalOrderStatus = payPalOrderStatus ?? PayPalOrderStatus;
        FulfillmentStatus = OrderFulfillmentStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var refund = new OrderRefund(payPalRefundId, idempotencyKey, amount, status);
        _refunds.Add(refund);
        RefreshRefundStatus();
        return refund;
    }

    private void RefreshRefundStatus()
    {
        if (FulfillmentStatus is not OrderFulfillmentStatus.Fulfilled
            and not OrderFulfillmentStatus.PartiallyRefunded
            and not OrderFulfillmentStatus.Refunded)
        {
            return;
        }

        if (RemainingRefundable() <= 0m)
        {
            FulfillmentStatus = OrderFulfillmentStatus.Refunded;
            PayPalCaptureStatus = "REFUNDED";
        }
        else
        {
            FulfillmentStatus = OrderFulfillmentStatus.PartiallyRefunded;
            PayPalCaptureStatus = "PARTIALLY_REFUNDED";
        }
    }
}

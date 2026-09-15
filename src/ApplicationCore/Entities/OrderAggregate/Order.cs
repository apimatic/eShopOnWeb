using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Order() {}

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
        : this(buyerId, shipToAddress, items, string.Empty)
    {
    }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Currency = currency;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public string Currency { get; private set; }
    public OrderPaymentState PaymentState { get; private set; } = OrderPaymentState.AwaitingPayment;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? PayPalCreateRequestId { get; private set; }
    public string? PayPalAuthorizeRequestId { get; private set; }
    public string? PayPalCaptureRequestId { get; private set; }
    public string? PayPalVoidRequestId { get; private set; }
    public string? PayPalReauthorizeRequestId { get; private set; }

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

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void PreparePayment(string currency, string createRequestId, string authorizeRequestId)
    {
        if (PaymentState != OrderPaymentState.AwaitingPayment && PaymentState != OrderPaymentState.AuthorizationPending)
            throw new InvalidOperationException("This order is not awaiting payment.");

        if (string.IsNullOrWhiteSpace(Currency)) Currency = currency;
        if (!string.Equals(Currency, currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The order currency does not match the configured payment currency.");

        PayPalCreateRequestId ??= createRequestId;
        PayPalAuthorizeRequestId ??= authorizeRequestId;
        PaymentState = OrderPaymentState.AuthorizationPending;
    }

    public void RecordPayPalOrder(string payPalOrderId) => PayPalOrderId = payPalOrderId;

    public void RecordAuthorization(string authorizationId, string status, decimal amount,
        DateTimeOffset? expiration)
    {
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationExpiration = expiration;
        PaymentState = string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase)
            ? OrderPaymentState.AuthorizationPending
            : OrderPaymentState.Authorized;
    }

    public void PrepareCapture(string requestId)
    {
        if (PaymentState != OrderPaymentState.Authorized && PaymentState != OrderPaymentState.CapturePending)
            throw new InvalidOperationException("The order does not have an authorization that can be captured.");
        PayPalCaptureRequestId ??= requestId;
        PaymentState = OrderPaymentState.CapturePending;
    }

    public void RecordCapture(string captureId, string status, decimal amount,
        decimal? fee, decimal? net)
    {
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetProceeds = net;
        if (string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            PaymentState = OrderPaymentState.Fulfilled;
            FulfilledAt ??= DateTimeOffset.UtcNow;
        }
        else
        {
            PaymentState = OrderPaymentState.CapturePending;
        }
    }

    public void RecordReauthorization(string authorizationId, string status, decimal amount,
        DateTimeOffset? expiration)
        => RecordAuthorization(authorizationId, status, amount, expiration);

    public string PrepareReauthorization(string requestId)
    {
        PayPalReauthorizeRequestId ??= requestId;
        return PayPalReauthorizeRequestId;
    }

    public void PrepareVoid(string requestId)
    {
        if (PaymentState != OrderPaymentState.Authorized && PaymentState != OrderPaymentState.AuthorizationPending)
            throw new InvalidOperationException("Only an uncaptured authorization can be cancelled.");
        PayPalVoidRequestId ??= requestId;
    }

    public void RecordVoid(string status)
    {
        PayPalAuthorizationStatus = status;
        if (string.Equals(status, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            PaymentState = OrderPaymentState.Cancelled;
            CancelledAt = DateTimeOffset.UtcNow;
        }
    }

    public void CancelWithoutAuthorization()
    {
        if (PaymentState != OrderPaymentState.AwaitingPayment &&
            !(PaymentState == OrderPaymentState.AuthorizationPending && PayPalAuthorizationId is null))
            throw new InvalidOperationException("Only an unpaid order can be cancelled without releasing an authorization.");
        PaymentState = OrderPaymentState.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public void RecordCompletedRefund(decimal amount)
    {
        if (CapturedAmount is null || RefundedAmount + amount > CapturedAmount.Value)
            throw new InvalidOperationException("The refund would exceed the captured amount.");
        RefundedAmount += amount;
        PaymentState = RefundedAmount == CapturedAmount.Value
            ? OrderPaymentState.Refunded
            : OrderPaymentState.PartiallyRefunded;
    }
}

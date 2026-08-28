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
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public string PaymentReference { get; private set; } = Guid.NewGuid().ToString("N");
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;
    public FulfilmentStatus FulfilmentStatus { get; private set; } = FulfilmentStatus.Unfulfilled;
    public string? PaymentCurrency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? MerchantNetAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx 
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void RecordAuthorization(string payPalOrderId, string payPalOrderStatus,
        string authorizationId, string authorizationStatus, string currency,
        DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        if (PaymentStatus != PaymentStatus.AwaitingPayment)
        {
            throw new InvalidOperationException("Only an order awaiting payment can be authorized.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        PaymentCurrency = currency;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = PaymentStatus.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string authorizationStatus,
        DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        if (PaymentStatus != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException("Only an authorized order can be reauthorized.");
        }

        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount,
        decimal fee, decimal netAmount, DateTimeOffset fulfilledAt)
    {
        if (PaymentStatus != PaymentStatus.Authorized || FulfilmentStatus != FulfilmentStatus.Unfulfilled)
        {
            throw new InvalidOperationException("Only an authorized, unfulfilled order can be captured.");
        }

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        PayPalAuthorizationStatus = "CAPTURED";
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        MerchantNetAmount = netAmount;
        PaymentStatus = PaymentStatus.Captured;
        FulfilmentStatus = FulfilmentStatus.Fulfilled;
        FulfilledAt = fulfilledAt;
    }

    public void RecordCancellation(string authorizationStatus, DateTimeOffset cancelledAt)
    {
        if (PaymentStatus != PaymentStatus.AwaitingPayment && PaymentStatus != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException("A fulfilled or refunded order cannot be cancelled.");
        }

        PayPalAuthorizationStatus = authorizationStatus;
        PaymentStatus = PaymentStatus.Voided;
        FulfilmentStatus = FulfilmentStatus.Cancelled;
        CancelledAt = cancelledAt;
    }

    public PaymentRefund RecordRefund(string refundId, string idempotencyKey, decimal amount,
        string status, DateTimeOffset createdAt)
    {
        if (PaymentStatus != PaymentStatus.Captured && PaymentStatus != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException("Only a captured payment can be refunded.");
        }

        var captured = CapturedAmount ?? 0m;
        if (amount <= 0 || RefundedAmount + amount > captured)
        {
            throw new InvalidOperationException("The refund would exceed the captured amount.");
        }

        var refund = new PaymentRefund(refundId, idempotencyKey, amount, status, createdAt);
        _refunds.Add(refund);
        RefundedAmount += amount;
        PaymentStatus = RefundedAmount == captured ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }
}

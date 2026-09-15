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
        PaymentReference = Guid.NewGuid().ToString("N");
        ShipToAddress = shipToAddress;
        _orderItems = items;
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
        FulfillmentStatus = OrderFulfillmentStatus.Unfulfilled;
    }

    public string BuyerId { get; private set; }
    public string PaymentReference { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderPaymentStatus PaymentStatus { get; private set; }
    public OrderFulfillmentStatus FulfillmentStatus { get; private set; }
    public string? PaymentCurrency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public bool AuthorizationWasRenewed { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public int? PaymentMethodId { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

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

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public decimal RefundedTotal() => _refunds
        .Where(r => r.Status is "COMPLETED" or "PENDING")
        .Sum(r => r.Amount);

    public PaymentRefund? FindRefund(string idempotencyKey) =>
        _refunds.SingleOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void RecordPayPalOrder(string payPalOrderId, string status, string currency)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = status;
        PaymentCurrency = currency;
    }

    public void RecordAuthorization(string authorizationId, string authorizationStatus,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt, int? paymentMethodId, string orderStatus,
        bool renewed = false)
    {
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        AuthorizationWasRenewed = AuthorizationWasRenewed || renewed;
        PaymentMethodId = paymentMethodId;
        PayPalOrderStatus = orderStatus;
        PaymentStatus = authorizationStatus == "PENDING"
            ? OrderPaymentStatus.AuthorizationPending
            : OrderPaymentStatus.Authorized;
    }

    public void RecordAuthorizationStatus(string status, DateTimeOffset? expiresAt = null)
    {
        PayPalAuthorizationStatus = status;
        if (expiresAt.HasValue) AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string status, decimal amount,
        decimal? payPalFee, decimal? netProceeds, DateTimeOffset? capturedAt)
    {
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = payPalFee;
        NetProceeds = netProceeds;
        CapturedAt = capturedAt;
        PayPalAuthorizationStatus = "CAPTURED";
        if (status == "COMPLETED")
        {
            PaymentStatus = OrderPaymentStatus.Captured;
            FulfillmentStatus = OrderFulfillmentStatus.Fulfilled;
            FulfilledAt = DateTimeOffset.UtcNow;
        }
        else
        {
            PaymentStatus = OrderPaymentStatus.CapturePending;
        }
    }

    public void Cancel(string? authorizationStatus = null)
    {
        if (authorizationStatus is not null) PayPalAuthorizationStatus = authorizationStatus;
        PaymentStatus = PayPalAuthorizationId is null
            ? OrderPaymentStatus.Cancelled
            : OrderPaymentStatus.Voided;
        FulfillmentStatus = OrderFulfillmentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund BeginRefund(string idempotencyKey, decimal amount)
    {
        var refund = new PaymentRefund(idempotencyKey, amount);
        _refunds.Add(refund);
        return refund;
    }

    public void RefreshRefundState()
    {
        var refunded = RefundedTotal();
        if (CapturedAmount.HasValue && refunded >= CapturedAmount.Value)
            PaymentStatus = OrderPaymentStatus.Refunded;
        else if (refunded > 0)
            PaymentStatus = OrderPaymentStatus.PartiallyRefunded;
    }
}

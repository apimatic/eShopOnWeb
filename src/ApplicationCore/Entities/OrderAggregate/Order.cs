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
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
        FulfilmentStatus = OrderFulfilmentStatus.Unfulfilled;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderPaymentStatus PaymentStatus { get; private set; }
    public OrderFulfilmentStatus FulfilmentStatus { get; private set; }
    public string? Currency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? AuthorizationStatusReason { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public string? CaptureStatusReason { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public string? PaymentFailureReason { get; private set; }

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

    public void RecordPayPalOrder(string payPalOrderId, string status, string currency)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = status;
        Currency = currency;
    }

    public void RecordAuthorization(string authorizationId, string status, decimal amount,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt, string? reason)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationStatusReason = reason;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentFailureReason = null;
        PaymentStatus = status == "PENDING" ? OrderPaymentStatus.AuthorizationPending : OrderPaymentStatus.Authorized;
    }

    public void RecordPaymentChallenge(string payPalOrderStatus)
    {
        PayPalOrderStatus = payPalOrderStatus;
        PaymentStatus = OrderPaymentStatus.PayerActionRequired;
        PaymentFailureReason = "PayPal requires browser approval; submit payment again with a card that does not require a challenge.";
    }

    public void RecordPaymentFailure(string reason)
    {
        PaymentFailureReason = reason;
        PaymentStatus = OrderPaymentStatus.PaymentFailed;
    }

    public void MarkAuthorizationExpired(string reason)
    {
        PaymentFailureReason = reason;
        PaymentStatus = OrderPaymentStatus.AuthorizationExpired;
    }

    public void RecordCapture(string captureId, string status, decimal amount, decimal? payPalFee,
        decimal? netProceeds, DateTimeOffset? capturedAt, string? reason)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CaptureStatusReason = reason;
        CapturedAmount = amount;
        PayPalFee = payPalFee;
        NetProceeds = netProceeds;
        CapturedAt = capturedAt;
        PaymentStatus = status == "COMPLETED" ? OrderPaymentStatus.Captured : OrderPaymentStatus.CapturePending;
        FulfilmentStatus = OrderFulfilmentStatus.Fulfilled;
    }

    public void RecordVoid(string status)
    {
        AuthorizationStatus = status;
        PaymentStatus = OrderPaymentStatus.Voided;
        FulfilmentStatus = OrderFulfilmentStatus.Cancelled;
    }

    public void CancelWithoutAuthorization()
    {
        PaymentStatus = OrderPaymentStatus.Voided;
        FulfilmentStatus = OrderFulfilmentStatus.Cancelled;
    }

    public PaymentRefund BeginRefund(string idempotencyKey, decimal amount)
    {
        var refund = new PaymentRefund(idempotencyKey, amount, Currency ?? string.Empty);
        _refunds.Add(refund);
        return refund;
    }

    public void RecalculateRefundStatus()
    {
        var refunded = 0m;
        foreach (var refund in _refunds)
        {
            if (refund.Status is not ("FAILED" or "CANCELLED")) refunded += refund.Amount;
        }

        if (CapturedAmount.HasValue && refunded >= CapturedAmount.Value)
        {
            PaymentStatus = OrderPaymentStatus.Refunded;
        }
        else if (refunded > 0)
        {
            PaymentStatus = OrderPaymentStatus.PartiallyRefunded;
        }
    }
}

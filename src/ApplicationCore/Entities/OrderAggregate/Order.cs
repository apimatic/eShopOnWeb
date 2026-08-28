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
        PaymentReference = Guid.NewGuid();
        PaymentStatus = PaymentStatus.AwaitingPayment;
        FulfillmentStatus = FulfillmentStatus.Pending;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public FulfillmentStatus FulfillmentStatus { get; private set; }
    public Guid PaymentReference { get; private set; }
    public string? PaymentCurrency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public byte[]? RowVersion { get; private set; }

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

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void EnsurePaymentReference()
    {
        if (PaymentReference == Guid.Empty)
        {
            PaymentReference = Guid.NewGuid();
        }
    }

    public void RecordAuthorization(string currency, string payPalOrderId, string orderStatus,
        string authorizationId, string authorizationStatus, DateTimeOffset authorizedAt,
        DateTimeOffset? expiresAt)
    {
        if (PaymentStatus != PaymentStatus.AwaitingPayment)
        {
            throw new InvalidOperationException("Only an order awaiting payment can be authorized.");
        }

        PaymentCurrency = currency;
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = orderStatus;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = authorizationStatus == "CREATED"
            ? PaymentStatus.Authorized
            : PaymentStatus.AuthorizationPending;
    }

    public void RecordReauthorization(string authorizationId, string status,
        DateTimeOffset authorizedAt, DateTimeOffset? expiresAt)
    {
        if (PaymentStatus is not (PaymentStatus.Authorized or PaymentStatus.AuthorizationPending or PaymentStatus.CapturePending))
        {
            throw new InvalidOperationException("This order does not have an authorization that can be renewed.");
        }

        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = status == "CREATED" ? PaymentStatus.Authorized : PaymentStatus.AuthorizationPending;
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount,
        decimal? payPalFee, decimal? netProceeds, DateTimeOffset capturedAt)
    {
        if (PaymentStatus is not (PaymentStatus.Authorized or PaymentStatus.AuthorizationPending))
        {
            throw new InvalidOperationException("This order is not authorized for capture.");
        }

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetProceeds = netProceeds;
        CapturedAt = capturedAt;
        PaymentStatus = status == "COMPLETED" ? PaymentStatus.Captured : PaymentStatus.CapturePending;
        if (status == "COMPLETED")
        {
            FulfillmentStatus = FulfillmentStatus.Fulfilled;
            FulfilledAt = DateTimeOffset.UtcNow;
        }
    }

    public void RecordVoid(string authorizationStatus)
    {
        if (FulfillmentStatus == FulfillmentStatus.Fulfilled)
        {
            throw new InvalidOperationException("A fulfilled order cannot be cancelled; refund its capture instead.");
        }

        PayPalAuthorizationStatus = authorizationStatus;
        PaymentStatus = PaymentStatus.Voided;
        FulfillmentStatus = FulfillmentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public void CancelUnpaid()
    {
        if (PaymentStatus != PaymentStatus.AwaitingPayment)
        {
            throw new InvalidOperationException("Only an unpaid order can be cancelled without voiding an authorization.");
        }

        PaymentStatus = PaymentStatus.Cancelled;
        FulfillmentStatus = FulfillmentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public void RecordRefund(PaymentRefund refund)
    {
        if (CapturedAmount is null || PaymentStatus is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new InvalidOperationException("Only a captured order can be refunded.");
        }

        if (refund.Amount <= 0 || RefundedAmount + refund.Amount > CapturedAmount.Value)
        {
            throw new InvalidOperationException("The refund exceeds the remaining captured amount.");
        }

        _refunds.Add(refund);
        RefundedAmount += refund.Amount;
        PaymentStatus = RefundedAmount == CapturedAmount.Value
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        PayPalCaptureStatus = PaymentStatus == PaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
    }

    public void UpdateRefundStatus(string payPalRefundId, string status)
    {
        var refund = _refunds.Find(r => r.PayPalRefundId == payPalRefundId)
            ?? throw new InvalidOperationException("The refund does not belong to this order.");
        var previousStatus = refund.Status;
        refund.SetStatus(status);
        if (previousStatus == "PENDING" && status is "FAILED" or "CANCELLED")
        {
            RefundedAmount -= refund.Amount;
            PaymentStatus = RefundedAmount == 0 ? PaymentStatus.Captured : PaymentStatus.PartiallyRefunded;
            PayPalCaptureStatus = RefundedAmount == 0 ? "COMPLETED" : "PARTIALLY_REFUNDED";
        }
    }
}

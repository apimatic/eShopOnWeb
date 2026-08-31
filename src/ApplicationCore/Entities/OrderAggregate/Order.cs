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
        PaymentStatus = OrderPaymentStatus.NotRequired;
        Currency = string.Empty;
    }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string currency)
        : this(buyerId, shipToAddress, items)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        Currency = currency.ToUpperInvariant();
        PaymentReference = Guid.NewGuid().ToString("N");
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderPaymentStatus PaymentStatus { get; private set; }
    public string Currency { get; private set; }
    public string? PaymentReference { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int AuthorizationRevision { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }

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

    public void StartPayment(string payPalOrderId, string payPalOrderStatus, int? savedPaymentMethodId)
    {
        if (PaymentStatus != OrderPaymentStatus.AwaitingPayment || PayPalOrderId != null)
        {
            throw new InvalidOperationException("This order has already started payment.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        SavedPaymentMethodId = savedPaymentMethodId;
        PaymentStatus = OrderPaymentStatus.AuthorizationPending;
    }

    public void RecordAuthorization(string authorizationId, string status,
        DateTimeOffset createdAt, DateTimeOffset expiresAt, decimal amount)
    {
        if (amount != Total())
        {
            throw new InvalidOperationException("PayPal authorized an amount that does not equal the order total.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        PayPalOrderStatus = "COMPLETED";
        PaymentStatus = status == "CREATED"
            ? OrderPaymentStatus.Authorized
            : OrderPaymentStatus.AuthorizationPending;
    }

    public void RecordReauthorization(string authorizationId, string status,
        DateTimeOffset createdAt, DateTimeOffset expiresAt, decimal amount)
    {
        var originalAuthorizationDeadline = AuthorizationExpiresAt;
        RecordAuthorization(authorizationId, status, createdAt, expiresAt, amount);
        if (originalAuthorizationDeadline != null && AuthorizationExpiresAt > originalAuthorizationDeadline)
        {
            AuthorizationExpiresAt = originalAuthorizationDeadline;
        }
        AuthorizationRevision++;
    }

    public void RecordCapturePending(string captureId, string status)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        PaymentStatus = OrderPaymentStatus.CapturePending;
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount,
        decimal payPalFee, decimal netProceeds, DateTimeOffset capturedAt)
    {
        if (capturedAmount != Total())
        {
            throw new InvalidOperationException("PayPal captured an amount that does not equal the order total.");
        }

        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetProceeds = netProceeds;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void Cancel(DateTimeOffset cancelledAt)
    {
        if (PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.CapturePending
            or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
        {
            throw new InvalidOperationException("A captured order cannot be cancelled; refund it instead.");
        }

        AuthorizationStatus = AuthorizationId == null ? AuthorizationStatus : "VOIDED";
        PaymentStatus = OrderPaymentStatus.Cancelled;
        CancelledAt = cancelledAt;
    }

    public PaymentRefund AddRefund(string payPalRefundId, string idempotencyKey,
        string status, decimal amount, DateTimeOffset createdAt)
    {
        if (CapturedAmount == null || CaptureId == null)
        {
            throw new InvalidOperationException("Only a captured order can be refunded.");
        }

        if (amount <= 0 || RefundedAmount + amount > CapturedAmount.Value)
        {
            throw new InvalidOperationException("The refund exceeds the remaining captured amount.");
        }

        var refund = new PaymentRefund(payPalRefundId, idempotencyKey, status, amount, Currency, createdAt);
        _refunds.Add(refund);
        RefundedAmount += amount;
        PaymentStatus = RefundedAmount == CapturedAmount.Value
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
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

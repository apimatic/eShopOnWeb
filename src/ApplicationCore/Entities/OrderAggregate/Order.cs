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
        : this(buyerId, shipToAddress, items, false, null)
    {
    }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, bool paymentRequired, string? currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        PaymentStatus = paymentRequired ? PaymentStatus.AwaitingPayment : PaymentStatus.NotRequired;
        FulfillmentStatus = FulfillmentStatus.AwaitingFulfillment;
        Currency = paymentRequired ? Guard.Against.NullOrEmpty(currency, nameof(currency)).ToUpperInvariant() : null;
        PaymentReference = paymentRequired ? $"eshop-{Guid.NewGuid():N}" : null;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public FulfillmentStatus FulfillmentStatus { get; private set; }
    public string? Currency { get; private set; }
    public string? PaymentReference { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? InitialAuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int ReauthorizationCount { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount { get; private set; }
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

    public void RecordPayPalOrder(string payPalOrderId, string status)
    {
        if (PaymentStatus != PaymentStatus.AwaitingPayment && PayPalOrderId != payPalOrderId)
            throw new InvalidOperationException("This order cannot start another PayPal payment.");

        PayPalOrderId = Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderStatus = Guard.Against.NullOrEmpty(status, nameof(status));
    }

    public void RecordAuthorization(string authorizationId, string status, decimal amount,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        var expected = decimal.Round(Total(), 2, MidpointRounding.AwayFromZero);
        if (amount != expected)
            throw new InvalidOperationException($"PayPal authorized {amount:F2}, but the order total is {expected:F2}.");

        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = Guard.Against.NullOrEmpty(status, nameof(status));
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt;
        InitialAuthorizationCreatedAt ??= createdAt;
        AuthorizationExpiresAt = expiresAt;
        PayPalOrderStatus = "COMPLETED";
        PaymentStatus = status == "CREATED" ? PaymentStatus.Authorized : PaymentStatus.AuthorizationPending;
    }

    public void RecordReauthorization(string authorizationId, string status, decimal amount,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        var initialCreatedAt = InitialAuthorizationCreatedAt;
        RecordAuthorization(authorizationId, status, amount, createdAt, expiresAt);
        InitialAuthorizationCreatedAt = initialCreatedAt ?? createdAt;
        ReauthorizationCount++;
    }

    public void RecordCapture(string captureId, string status, decimal amount, decimal? payPalFee,
        decimal? netProceeds, DateTimeOffset? capturedAt)
    {
        var expected = decimal.Round(Total(), 2, MidpointRounding.AwayFromZero);
        if (amount != expected)
            throw new InvalidOperationException($"PayPal captured {amount:F2}, but the order total is {expected:F2}.");

        CaptureId = Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureStatus = Guard.Against.NullOrEmpty(status, nameof(status));
        CapturedAmount = amount;
        PayPalFee = payPalFee;
        NetProceeds = netProceeds;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
        PaymentStatus = status == "COMPLETED" ? PaymentStatus.Captured : PaymentStatus.CapturePending;

        if (status == "COMPLETED")
        {
            FulfillmentStatus = FulfillmentStatus.Fulfilled;
            FulfilledAt = DateTimeOffset.UtcNow;
        }
    }

    public void RecordCancellation(string authorizationStatus)
    {
        if (FulfillmentStatus == FulfillmentStatus.Fulfilled)
            throw new InvalidOperationException("A fulfilled order cannot be cancelled; refund it instead.");

        AuthorizationStatus = authorizationStatus;
        PaymentStatus = AuthorizationId is null ? PaymentStatus.Cancelled : PaymentStatus.Voided;
        FulfillmentStatus = FulfillmentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund StartRefund(decimal amount, string idempotencyKey, string payPalRequestId)
    {
        if (PaymentStatus is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
            throw new InvalidOperationException("Only a captured payment can be refunded.");
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Refund amount must be positive.");

        var remaining = (CapturedAmount ?? 0) - RefundedAmount;
        if (amount > remaining)
            throw new InvalidOperationException($"The maximum refundable amount is {remaining:F2} {Currency}.");

        var refund = new PaymentRefund(amount, Currency!, idempotencyKey, payPalRequestId);
        _refunds.Add(refund);
        return refund;
    }

    public void CompleteRefund(PaymentRefund refund, string payPalRefundId, string status,
        decimal amount, DateTimeOffset? createdAt)
    {
        refund.Complete(payPalRefundId, status, amount, createdAt);
        if (status != "COMPLETED") return;

        RefundedAmount = _refunds.Where(r => r.Status == "COMPLETED").Sum(r => r.Amount);
        PaymentStatus = RefundedAmount == CapturedAmount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        CaptureStatus = PaymentStatus == PaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
    }
}

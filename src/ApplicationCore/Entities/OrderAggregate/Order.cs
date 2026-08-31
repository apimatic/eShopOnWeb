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
    public string? PaymentReference { get; private set; } = Guid.NewGuid().ToString("N");

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public OrderFulfilmentStatus FulfilmentStatus { get; private set; } = OrderFulfilmentStatus.Pending;
    public string? PaymentCurrency { get; private set; }
    public string? PaypalOrderId { get; private set; }
    public string? PaypalOrderStatus { get; private set; }
    public string? PaypalAuthorizationId { get; private set; }
    public string? PaypalAuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? PaypalCaptureId { get; private set; }
    public string? PaypalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public int AuthorizationAttempt { get; private set; }

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

    public void InitializePayment(string currency)
    {
        PaymentReference ??= Guid.NewGuid().ToString("N");
        PaymentCurrency = Guard.Against.NullOrEmpty(currency).ToUpperInvariant();
    }

    public void RecordPaypalOrder(string paypalOrderId, string paypalStatus)
    {
        if (PaymentStatus is not (OrderPaymentStatus.AwaitingPayment or OrderPaymentStatus.AuthorizationRenewalRequired))
            throw new InvalidOperationException("This order is not awaiting an authorization.");

        PaypalOrderId = Guard.Against.NullOrEmpty(paypalOrderId);
        PaypalOrderStatus = Guard.Against.NullOrEmpty(paypalStatus);
    }

    public void RecordAuthorization(string authorizationId, string status, decimal amount,
        DateTimeOffset authorizedAt, DateTimeOffset expirationTime)
    {
        if (decimal.Round(amount, 2) != decimal.Round(Total(), 2))
            throw new InvalidOperationException("PayPal authorized an amount different from the order total.");

        var isNewAuthorization = PaypalAuthorizationId != authorizationId;
        PaypalAuthorizationId = Guard.Against.NullOrEmpty(authorizationId);
        PaypalAuthorizationStatus = Guard.Against.NullOrEmpty(status);
        AuthorizedAmount = amount;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expirationTime;
        PaymentStatus = status == "CREATED"
            ? OrderPaymentStatus.Authorized
            : status == "PENDING"
                ? OrderPaymentStatus.AuthorizationPending
                : throw new InvalidOperationException($"PayPal authorization status '{status}' is not actionable.");
        if (isNewAuthorization) AuthorizationAttempt++;
    }

    public void RecordReauthorization(string authorizationId, string status, decimal amount,
        DateTimeOffset authorizedAt, DateTimeOffset expirationTime)
    {
        RecordAuthorization(authorizationId, status, amount, authorizedAt, expirationTime);
    }

    public void RequireNewAuthorization(string paypalStatus)
    {
        PaypalAuthorizationStatus = paypalStatus;
        PaymentStatus = OrderPaymentStatus.AuthorizationRenewalRequired;
        PaypalOrderId = null;
        PaypalOrderStatus = null;
    }

    public void RecordCapture(string captureId, string status, decimal amount, decimal fee,
        decimal netProceeds, DateTimeOffset capturedAt)
    {
        if (PaymentStatus is not (OrderPaymentStatus.Authorized or OrderPaymentStatus.CapturePending))
            throw new InvalidOperationException("Only an authorized order can be captured.");
        if (decimal.Round(amount, 2) != decimal.Round(Total(), 2))
            throw new InvalidOperationException("PayPal captured an amount different from the order total.");

        PaypalCaptureId = Guard.Against.NullOrEmpty(captureId);
        PaypalCaptureStatus = Guard.Against.NullOrEmpty(status);
        CapturedAmount = amount;
        PaypalFee = fee;
        NetProceeds = netProceeds;
        CapturedAt = capturedAt;
        if (status == "PENDING")
        {
            PaymentStatus = OrderPaymentStatus.CapturePending;
            return;
        }
        if (status != "COMPLETED")
            throw new InvalidOperationException($"PayPal capture status '{status}' is not successful.");

        FulfilledAt = capturedAt;
        PaymentStatus = OrderPaymentStatus.Captured;
        FulfilmentStatus = OrderFulfilmentStatus.Fulfilled;
    }

    public void Cancel(string paypalAuthorizationStatus, DateTimeOffset cancelledAt)
    {
        if (FulfilmentStatus == OrderFulfilmentStatus.Fulfilled)
            throw new InvalidOperationException("A fulfilled order cannot be cancelled; refund it instead.");

        PaypalAuthorizationStatus = paypalAuthorizationStatus;
        PaymentStatus = OrderPaymentStatus.Voided;
        FulfilmentStatus = OrderFulfilmentStatus.Cancelled;
        CancelledAt = cancelledAt;
    }

    public PaymentRefund RecordRefund(string paypalRefundId, string idempotencyKey, decimal amount,
        string paypalStatus, DateTimeOffset createdAt)
    {
        if (FulfilmentStatus != OrderFulfilmentStatus.Fulfilled || CapturedAmount is null)
            throw new InvalidOperationException("Only a fulfilled, captured order can be refunded.");
        if (amount <= 0 || decimal.Round(RefundedAmount + amount, 2) > decimal.Round(CapturedAmount.Value, 2))
            throw new InvalidOperationException("The refund exceeds the remaining captured amount.");

        var refund = new PaymentRefund(paypalRefundId, idempotencyKey, amount,
            Guard.Against.NullOrEmpty(PaymentCurrency), paypalStatus, createdAt);
        _refunds.Add(refund);
        RefundedAmount += amount;
        PaymentStatus = RefundedAmount == CapturedAmount.Value
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
        return refund;
    }
}

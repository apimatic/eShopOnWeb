using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Order() {}

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string currency = "")
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Currency = currency.ToUpperInvariant();
        ExternalReference = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public string ExternalReference { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;
    public FulfilmentStatus FulfilmentStatus { get; private set; } = FulfilmentStatus.Unfulfilled;
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public string? PaymentSourceDescription { get; private set; }

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

    public void RecordAuthorization(string paypalOrderId, string authorizationId, string status,
        decimal amount, DateTimeOffset createdAt, DateTimeOffset? expiresAt, string sourceDescription)
    {
        PayPalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentSourceDescription = sourceDescription;
        PaymentStatus = status switch
        {
            "CREATED" => PaymentStatus.Authorized,
            "DENIED" => PaymentStatus.AuthorizationDenied,
            _ => PaymentStatus.AuthorizationPending
        };
    }

    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = status == "CREATED" ? PaymentStatus.Authorized : PaymentStatus.AuthorizationPending;
    }

    public void RefreshAuthorization(string status)
    {
        AuthorizationStatus = status;
        PaymentStatus = status switch
        {
            "CREATED" => PaymentStatus.Authorized,
            "DENIED" => PaymentStatus.AuthorizationDenied,
            "VOIDED" => PaymentStatus.Voided,
            _ => PaymentStatus.AuthorizationPending
        };
    }

    public void RecordCapture(string captureId, string status, decimal amount, decimal fee, decimal net,
        DateTimeOffset capturedAt)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
        PaymentStatus = PaymentStatus.Captured;
        FulfilmentStatus = FulfilmentStatus.Fulfilled;
    }

    public void Cancel(bool authorizationWasVoided)
    {
        FulfilmentStatus = FulfilmentStatus.Cancelled;
        if (authorizationWasVoided)
        {
            AuthorizationStatus = "VOIDED";
            PaymentStatus = PaymentStatus.Voided;
        }
        else
        {
            PaymentStatus = PaymentStatus.Cancelled;
        }
    }

    public PaymentRefund AddRefund(string idempotencyKey, string paypalRefundId, string status,
        decimal amount, decimal? fee, decimal? net, DateTimeOffset createdAt)
    {
        var refund = new PaymentRefund(idempotencyKey, paypalRefundId, status, amount, fee, net, createdAt);
        _refunds.Add(refund);
        RefundedAmount += amount;
        PaymentStatus = CapturedAmount.HasValue && RefundedAmount >= CapturedAmount.Value
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}

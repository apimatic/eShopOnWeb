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
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public string PaymentStatus { get; private set; } = "AwaitingPayment";
    public string? PaymentAuthorizationId { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }
    public string? PaymentCaptureId { get; private set; }
    public decimal CapturedAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public decimal PaymentFee { get; private set; }
    public decimal NetProceeds { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public string RefundIdempotencyKeys { get; private set; } = string.Empty;
    public void SetAuthorization(string id) { PaymentAuthorizationId = id; PaymentStatus = "Authorized"; }
    public void SetSavedPaymentMethod(int? id) { SavedPaymentMethodId = id; }
    public void SetFulfilled(string captureId, decimal captured, decimal fee, decimal net) { PaymentCaptureId=captureId; CapturedAmount=captured; PaymentFee=fee; NetProceeds=net; PaymentStatus="Captured"; FulfilledAt=DateTimeOffset.UtcNow; }
    public bool AddRefund(decimal amount, string idempotencyKey) { if (RefundIdempotencyKeys.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(idempotencyKey)) return false; RefundedAmount += amount; RefundIdempotencyKeys = string.IsNullOrEmpty(RefundIdempotencyKeys) ? idempotencyKey : RefundIdempotencyKeys + "," + idempotencyKey; PaymentStatus = RefundedAmount >= CapturedAmount ? "Refunded" : "PartiallyRefunded"; return true; }
    public void Cancel() { PaymentStatus = "Cancelled"; }

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
}

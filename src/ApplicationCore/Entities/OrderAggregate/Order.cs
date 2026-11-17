using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

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
        AuthorizationIdempotencyKey = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    public string Status { get; private set; } = OrderStatuses.AwaitingPayment;
    public string AuthorizationIdempotencyKey { get; private set; }
    public string? Currency { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public long? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureTransferId { get; private set; }
    public string? CaptureState { get; private set; }
    public long? CapturedAmount { get; private set; }
    public long? FinixFee { get; private set; }
    public long? NetAmount { get; private set; }
    public string? PaymentInstrumentId { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }
    public long RefundedAmount { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

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

    public long TotalCents() => (long)Math.Round(Total() * 100m, MidpointRounding.AwayFromZero);

    public long RefundableAmount() => Math.Max(0, (CapturedAmount ?? 0) - RefundedAmount);

    public void SetCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(Currency))
        {
            Currency = currency;
        }
    }

    public void MarkAuthorized(string authorizationId,
        string authorizationState,
        long authorizedAmount,
        string currency,
        string paymentInstrumentId,
        int? savedPaymentMethodId,
        DateTimeOffset? expiresAt)
    {
        Status = OrderStatuses.Authorized;
        AuthorizationId = authorizationId;
        AuthorizationState = authorizationState;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        PaymentInstrumentId = paymentInstrumentId;
        SavedPaymentMethodId = savedPaymentMethodId;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateAuthorizationState(string authorizationState)
    {
        AuthorizationState = authorizationState;
    }

    public void MarkCaptured(string captureTransferId,
        string captureState,
        long capturedAmount,
        long finixFee,
        long netAmount)
    {
        Status = OrderStatuses.Fulfilled;
        CaptureTransferId = captureTransferId;
        CaptureState = captureState;
        CapturedAmount = capturedAmount;
        FinixFee = finixFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCancelled(string reason)
    {
        Status = OrderStatuses.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        CancellationReason = reason;
        if (AuthorizationId is not null && CaptureTransferId is null)
        {
            AuthorizationState = "VOIDED";
        }
    }

    public void AddRefund(long amount)
    {
        RefundedAmount += amount;
        Status = RefundedAmount >= (CapturedAmount ?? 0)
            ? OrderStatuses.Refunded
            : OrderStatuses.PartiallyRefunded;
    }
}

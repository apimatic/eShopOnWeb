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

    public string PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string PaymentCurrency { get; private set; } = string.Empty;
    public string? PaymentIdempotencyKey { get; private set; }
    public string? PaymentFailureMessage { get; private set; }

    public string? FinixAuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    public string? FinixCaptureId { get; private set; }
    public string? CaptureState { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? FinixFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public decimal RefundedAmount { get; private set; }
    public bool IsFulfilled { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }

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

    public void SetCurrency(string currency)
    {
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));
        PaymentCurrency = currency;
    }

    public void BeginPaymentAttempt(string idempotencyKey)
    {
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));
        PaymentIdempotencyKey = idempotencyKey;
        PaymentFailureMessage = null;
    }

    public void MarkPaymentAuthorized(string authorizationId, string state, decimal amount, DateTimeOffset authorizedAt)
    {
        FinixAuthorizationId = authorizationId;
        AuthorizationState = state;
        AuthorizedAmount = amount;
        AuthorizedAt = authorizedAt;
        PaymentStatus = OrderPaymentStatus.Authorized;
        PaymentFailureMessage = null;
    }

    public void UpdateAuthorizationState(string state)
    {
        AuthorizationState = state;
    }

    public void MarkPaymentFailed(string message)
    {
        PaymentStatus = OrderPaymentStatus.PaymentFailed;
        PaymentFailureMessage = message;
        PaymentIdempotencyKey = null;
    }

    public void MarkCaptured(string captureId, string captureState, decimal capturedAmount, decimal fee, decimal netProceeds, DateTimeOffset capturedAt)
    {
        FinixCaptureId = captureId;
        CaptureState = captureState;
        CapturedAmount = capturedAmount;
        FinixFee = fee;
        NetProceeds = netProceeds;
        CapturedAt = capturedAt;
        PaymentStatus = OrderPaymentStatus.Captured;
        PaymentFailureMessage = null;
    }

    public void MarkCaptureFailed(string message)
    {
        PaymentFailureMessage = message;
    }

    public void UpdateCaptureState(string state)
    {
        Guard.Against.NullOrWhiteSpace(state, nameof(state));
        CaptureState = state;
    }

    public void MarkFulfilled(DateTimeOffset fulfilledAt)
    {
        IsFulfilled = true;
        FulfilledAt = fulfilledAt;
    }

    public void MarkCanceled(DateTimeOffset canceledAt)
    {
        PaymentStatus = OrderPaymentStatus.Canceled;
        CanceledAt = canceledAt;
    }

    public void ApplyRefund(decimal refundedTotal)
    {
        RefundedAmount = refundedTotal;
        if (CapturedAmount.HasValue && refundedTotal >= CapturedAmount.Value)
        {
            PaymentStatus = OrderPaymentStatus.Refunded;
        }
        else if (refundedTotal > 0)
        {
            PaymentStatus = OrderPaymentStatus.PartiallyRefunded;
        }
    }
}

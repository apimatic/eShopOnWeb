using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderStatus
{
    AwaitingPayment = 0,
    Authorized = 1,
    Fulfilled = 2,
    Cancelled = 3,
    PartiallyRefunded = 4,
    Refunded = 5
}

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
        TotalAmountCents = ToCents(items.Sum(item => item.UnitPrice * item.Units));
        PaymentIdempotencyKey = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public string PaymentIdempotencyKey { get; private set; }

    public long TotalAmountCents { get; private set; }

    public string? PaymentAuthorizationId { get; private set; }
    public string? PaymentAuthorizationState { get; private set; }
    public string? PaymentVoidState { get; private set; }
    public string? PaymentCaptureTransferId { get; private set; }
    public string? PaymentCaptureState { get; private set; }
    public long PaymentAmountCents { get; private set; }
    public long PaymentCapturedAmountCents { get; private set; }
    public long PaymentFeeCents { get; private set; }
    public long PaymentNetCents { get; private set; }
    public string? PaymentCurrency { get; private set; }
    public string? PaymentInstrumentId { get; private set; }
    public int? PaymentMethodId { get; private set; }
    public string? PaymentFailureMessage { get; private set; }
    public int PaymentAttempt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
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

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public long RefundedAmountCents => _refunds
        .Where(refund => !string.Equals(refund.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
        .Sum(refund => refund.AmountCents);

    public long RefundableAmountCents => Math.Max(0, PaymentCapturedAmountCents - RefundedAmountCents);

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void MarkAuthorized(string authorizationId,
        string? authorizationState,
        string currency,
        long amountCents,
        string? instrumentId,
        int? paymentMethodId)
    {
        PaymentAuthorizationId = authorizationId;
        PaymentAuthorizationState = authorizationState;
        PaymentCurrency = currency;
        PaymentAmountCents = amountCents;
        PaymentInstrumentId = instrumentId;
        PaymentMethodId = paymentMethodId;
        PaymentFailureMessage = null;
        AuthorizedAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Authorized;
    }

    public void MarkPaymentFailed(string? message, bool incrementAttempt = true)
    {
        PaymentFailureMessage = message;
        if (incrementAttempt)
        {
            PaymentAttempt++;
        }
    }

    public void MarkFulfilled(string captureTransferId,
        string? captureState,
        long capturedAmountCents,
        long feeCents,
        long netCents)
    {
        PaymentCaptureTransferId = captureTransferId;
        PaymentCaptureState = captureState;
        PaymentCapturedAmountCents = capturedAmountCents;
        PaymentFeeCents = feeCents;
        PaymentNetCents = netCents;
        FulfilledAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled(string? authorizationState, string? voidState)
    {
        if (authorizationState is not null)
        {
            PaymentAuthorizationState = authorizationState;
        }

        PaymentVoidState = voidState;
        CancelledAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Cancelled;
    }

    public void AddRefund(OrderRefund refund)
    {
        if (!_refunds.Contains(refund))
        {
            _refunds.Add(refund);
        }

        if (RefundedAmountCents >= PaymentCapturedAmountCents && PaymentCapturedAmountCents > 0)
        {
            Status = OrderStatus.Refunded;
        }
        else if (RefundedAmountCents > 0)
        {
            Status = OrderStatus.PartiallyRefunded;
        }
    }

    public static long ToCents(decimal amount) =>
        (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    public string GetOrCreatePaymentIdempotencyKey()
    {
        if (string.IsNullOrWhiteSpace(PaymentIdempotencyKey))
        {
            PaymentIdempotencyKey = Guid.NewGuid().ToString("N");
        }

        return PaymentIdempotencyKey;
    }
}

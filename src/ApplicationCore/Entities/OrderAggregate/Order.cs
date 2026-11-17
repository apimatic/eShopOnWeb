using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
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
        PaymentReference = Guid.NewGuid().ToString("N");
        PaymentStatus = OrderPaymentStatuses.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    /// <summary>
    /// Stable per-order reference used when deriving Finix idempotency keys.
    /// Unlike the integer key it stays unique across in-memory restarts.
    /// </summary>
    public string PaymentReference { get; private set; } = string.Empty;

    public string PaymentStatus { get; private set; } = OrderPaymentStatuses.AwaitingPayment;
    public string Currency { get; private set; } = string.Empty;
    public string? PaymentAuthorizationId { get; private set; }
    public string? PaymentAuthorizationState { get; private set; }
    public string? PaymentCaptureTransferId { get; private set; }
    public string? PaymentCaptureState { get; private set; }
    public string? PaymentInstrumentId { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }
    public long AuthorizedAmountCents { get; private set; }
    public long CapturedAmountCents { get; private set; }
    public long FinixFeeCents { get; private set; }
    public long NetProceedsCents { get; private set; }
    public long RefundedAmountCents { get; private set; }
    public string? PaymentFailureMessage { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }

    public long RemainingRefundableCents => CapturedAmountCents - RefundedAmountCents;

    public void SetCurrency(string currency)
    {
        Currency = currency;
    }

    public void MarkAuthorized(
        string authorizationId,
        string authorizationState,
        long amountCents,
        string paymentInstrumentId,
        int? savedPaymentMethodId)
    {
        PaymentAuthorizationId = authorizationId;
        PaymentAuthorizationState = authorizationState;
        AuthorizedAmountCents = amountCents;
        PaymentInstrumentId = paymentInstrumentId;
        SavedPaymentMethodId = savedPaymentMethodId;
        PaymentStatus = OrderPaymentStatuses.Authorized;
        PaymentFailureMessage = null;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public void RecordPaymentFailure(string? authorizationId, string? authorizationState, string message)
    {
        PaymentAuthorizationId = authorizationId;
        PaymentAuthorizationState = authorizationState;
        PaymentFailureMessage = message;
        PaymentStatus = OrderPaymentStatuses.AwaitingPayment;
    }

    public void MarkFulfilled(
        string captureTransferId,
        string captureState,
        long capturedAmountCents,
        long finixFeeCents,
        long netProceedsCents)
    {
        PaymentCaptureTransferId = captureTransferId;
        PaymentCaptureState = captureState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = finixFeeCents;
        NetProceedsCents = netProceedsCents;
        PaymentStatus = OrderPaymentStatuses.Fulfilled;
        FulfilledAt = DateTimeOffset.UtcNow;
    }

    public void UpdateCaptureState(string captureState)
    {
        PaymentCaptureState = captureState;
    }

    public void MarkCanceled(string? authorizationState)
    {
        PaymentStatus = OrderPaymentStatuses.Canceled;
        if (!string.IsNullOrWhiteSpace(authorizationState))
        {
            PaymentAuthorizationState = authorizationState;
        }
        CanceledAt = DateTimeOffset.UtcNow;
    }

    public void ApplyRefund(long amountCents)
    {
        RefundedAmountCents += amountCents;
        PaymentStatus = RefundedAmountCents >= CapturedAmountCents
            ? OrderPaymentStatuses.Refunded
            : OrderPaymentStatuses.PartiallyRefunded;
    }

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

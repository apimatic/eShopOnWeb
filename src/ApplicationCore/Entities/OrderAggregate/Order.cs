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

    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public string Currency { get; private set; } = "USD";

    // State owned by Finix. These identifiers let any later request (fulfil, cancel, refund,
    // reconciliation) act on the payment without the request that created it being present.
    public string? PaymentInstrumentId { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public string? CaptureTransferId { get; private set; }
    public string? CaptureTransferState { get; private set; }
    public long? AuthorizedAmountCents { get; private set; }
    public long? CapturedAmountCents { get; private set; }
    public long? FinixFeeCents { get; private set; }
    public long? NetProceedsCents { get; private set; }
    public long RefundedAmountCents { get; private set; }
    public string? PaymentFailureMessage { get; private set; }

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

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
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

    public void SetCurrency(string currency)
    {
        if (!string.IsNullOrWhiteSpace(currency))
        {
            Currency = currency;
        }
    }

    public void RecordPaymentFailure(string message)
    {
        PaymentFailureMessage = message;
    }

    public void MarkAuthorized(string authorizationId, string? authorizationState,
        string paymentInstrumentId, int? savedPaymentMethodId, long authorizedAmountCents, string currency)
    {
        AuthorizationId = authorizationId;
        AuthorizationState = authorizationState;
        PaymentInstrumentId = paymentInstrumentId;
        SavedPaymentMethodId = savedPaymentMethodId;
        AuthorizedAmountCents = authorizedAmountCents;
        Currency = currency;
        PaymentFailureMessage = null;
        Status = OrderStatus.Authorized;
    }

    public void MarkAuthorizationUnusable(string message)
    {
        PaymentFailureMessage = message;
        AuthorizationState = "UNUSABLE";
        Status = OrderStatus.AwaitingPayment;
    }

    public void MarkFulfilled(string captureTransferId, string? captureTransferState,
        long capturedAmountCents, long finixFeeCents, long netProceedsCents)
    {
        CaptureTransferId = captureTransferId;
        CaptureTransferState = captureTransferState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = finixFeeCents;
        NetProceedsCents = netProceedsCents;
        PaymentFailureMessage = null;
        Status = OrderStatus.Fulfilled;
    }

    public void UpdateCaptureOutcome(string? captureTransferState, long capturedAmountCents,
        long finixFeeCents, long netProceedsCents)
    {
        CaptureTransferState = captureTransferState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = finixFeeCents;
        NetProceedsCents = netProceedsCents;
    }

    public void UpdateAuthorizationState(string? authorizationState)
    {
        if (!string.IsNullOrWhiteSpace(authorizationState))
        {
            AuthorizationState = authorizationState;
        }
    }

    public void MarkCancelled(string? message = null)
    {
        PaymentFailureMessage = message;
        Status = OrderStatus.Cancelled;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
    }

    public PaymentRefund AddRefund(string refundId, long amountCents, string? status,
        string idempotencyKey, DateTimeOffset createdAt)
    {
        var refund = new PaymentRefund(Id, refundId, amountCents, status, idempotencyKey, createdAt);
        _refunds.Add(refund);
        RefundedAmountCents += amountCents;

        Status = CapturedAmountCents.HasValue && RefundedAmountCents >= CapturedAmountCents.Value
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;

        return refund;
    }
}

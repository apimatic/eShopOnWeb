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
        TotalCents = Money.ToCents(Total());
        PaymentReference = Guid.NewGuid();
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new list. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx 
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    public Guid PaymentReference { get; private set; } = Guid.NewGuid();
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string Currency { get; private set; } = "USD";
    public long TotalCents { get; private set; }
    public long AuthorizedAmountCents { get; private set; }
    public long CapturedAmountCents { get; private set; }
    public long RefundedAmountCents { get; private set; }
    public long FeeCents { get; private set; }
    public long NetCents { get; private set; }
    public string? FinixAuthorizationId { get; private set; }
    public string? FinixAuthorizationState { get; private set; }
    public string? FinixTransferId { get; private set; }
    public string? FinixCaptureState { get; private set; }
    public string? FinixPaymentInstrumentId { get; private set; }
    public string? PaymentFailureCode { get; private set; }
    public string? PaymentFailureMessage { get; private set; }

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
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Currency = currency;
    }

    public void MarkAuthorized(string authorizationId, string paymentInstrumentId, long authorizedAmountCents, string? state = null)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(paymentInstrumentId, nameof(paymentInstrumentId));
        Guard.Against.NegativeOrZero(authorizedAmountCents, nameof(authorizedAmountCents));

        PaymentStatus = OrderPaymentStatus.Authorized;
        FinixAuthorizationId = authorizationId;
        FinixAuthorizationState = state;
        FinixPaymentInstrumentId = paymentInstrumentId;
        AuthorizedAmountCents = authorizedAmountCents;
        PaymentFailureCode = null;
        PaymentFailureMessage = null;
    }

    public void MarkPaymentFailed(string? failureCode, string? failureMessage)
    {
        PaymentStatus = OrderPaymentStatus.PaymentFailed;
        PaymentFailureCode = failureCode;
        PaymentFailureMessage = failureMessage;
    }

    public void MarkCaptureUnavailable(string? failureCode, string? failureMessage)
    {
        PaymentFailureCode = failureCode;
        PaymentFailureMessage = failureMessage;
    }

    public void MarkCaptured(string transferId, long capturedAmountCents, long feeCents, long netCents, string? state = null)
    {
        Guard.Against.NullOrEmpty(transferId, nameof(transferId));
        Guard.Against.NegativeOrZero(capturedAmountCents, nameof(capturedAmountCents));

        PaymentStatus = OrderPaymentStatus.Captured;
        FinixTransferId = transferId;
        FinixCaptureState = state;
        CapturedAmountCents = capturedAmountCents;
        FeeCents = feeCents;
        NetCents = netCents;
        PaymentFailureCode = null;
        PaymentFailureMessage = null;
    }

    public void MarkCancelled()
    {
        PaymentStatus = OrderPaymentStatus.Cancelled;
        FinixAuthorizationState = "CANCELED";
        PaymentFailureCode = null;
        PaymentFailureMessage = null;
    }

    public void MarkRefunded(long refundAmountCents)
    {
        Guard.Against.NegativeOrZero(refundAmountCents, nameof(refundAmountCents));

        RefundedAmountCents += refundAmountCents;
        PaymentStatus = RefundedAmountCents >= CapturedAmountCents
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
    }
}

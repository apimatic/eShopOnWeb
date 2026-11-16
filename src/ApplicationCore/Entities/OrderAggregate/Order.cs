using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
    public const string StatusAwaitingPayment = "AwaitingPayment";
    public const string StatusAuthorized = "Authorized";
    public const string StatusFulfilled = "Fulfilled";
    public const string StatusCancelled = "Cancelled";

    public const string PaymentStatusAwaitingPayment = "AwaitingPayment";
    public const string PaymentStatusAuthorized = "Authorized";
    public const string PaymentStatusCaptured = "Captured";
    public const string PaymentStatusPaymentFailed = "PaymentFailed";
    public const string PaymentStatusAuthorizationUnusable = "AuthorizationUnusable";
    public const string PaymentStatusCancelled = "Cancelled";
    public const string PaymentStatusPartiallyRefunded = "PartiallyRefunded";
    public const string PaymentStatusRefunded = "Refunded";

    #pragma warning disable CS8618 // Required by Entity Framework
    private Order() {}

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        AmountCents = ToCents(Total());
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    public string Status { get; private set; } = StatusAwaitingPayment;
    public string PaymentStatus { get; private set; } = PaymentStatusAwaitingPayment;
    public string Currency { get; private set; } = string.Empty;
    public int AmountCents { get; private set; }
    public string PaymentAttemptKey { get; private set; } = Guid.NewGuid().ToString("N");
    public string PaymentReference { get; private set; } = Guid.NewGuid().ToString("N");

    public string? FinixAuthorizationId { get; private set; }
    public string? FinixAuthorizationState { get; private set; }
    public string? FinixPaymentInstrumentId { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? FinixCaptureTransferId { get; private set; }
    public string? FinixCaptureState { get; private set; }
    public int? CapturedAmountCents { get; private set; }
    public int? FinixFeeCents { get; private set; }
    public int? NetProceedsCents { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }

    public int RefundedAmountCents { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
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

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void InitializePayment(int amountCents, string currency)
    {
        Guard.Against.Negative(amountCents, nameof(amountCents));
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));

        AmountCents = amountCents;
        Currency = currency;
        Status = StatusAwaitingPayment;
        PaymentStatus = PaymentStatusAwaitingPayment;
        PaymentFailureMessage = null;
    }

    public void MarkAuthorized(string authorizationId, string authorizationState, string paymentInstrumentId,
        int authorizedAmountCents, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrWhiteSpace(authorizationId, nameof(authorizationId));

        FinixAuthorizationId = authorizationId;
        FinixAuthorizationState = authorizationState;
        FinixPaymentInstrumentId = paymentInstrumentId;
        AuthorizedAt = DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = expiresAt;
        AmountCents = authorizedAmountCents;
        Status = StatusAuthorized;
        PaymentStatus = PaymentStatusAuthorized;
        PaymentFailureMessage = null;
    }

    public void MarkPaymentFailed(string message)
    {
        PaymentStatus = PaymentStatusPaymentFailed;
        PaymentFailureMessage = message;
        RotatePaymentAttemptKey();
    }

    public void MarkAuthorizationUnusable(string message)
    {
        PaymentStatus = PaymentStatusAuthorizationUnusable;
        PaymentFailureMessage = message;
    }

    public void RotatePaymentAttemptKey()
    {
        PaymentAttemptKey = Guid.NewGuid().ToString("N");
    }

    public void MarkFulfilled(string captureTransferId, string captureState, int capturedAmountCents,
        int feeCents, int netProceedsCents, DateTimeOffset fulfilledAt)
    {
        Guard.Against.NullOrWhiteSpace(captureTransferId, nameof(captureTransferId));

        FinixCaptureTransferId = captureTransferId;
        FinixCaptureState = captureState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = feeCents;
        NetProceedsCents = netProceedsCents;
        FulfilledAt = fulfilledAt;
        Status = StatusFulfilled;
        PaymentStatus = RefundedAmountCents <= 0
            ? PaymentStatusCaptured
            : RefundedAmountCents >= capturedAmountCents
                ? PaymentStatusRefunded
                : PaymentStatusPartiallyRefunded;
        PaymentFailureMessage = null;
    }

    public void MarkCancelled(string? authorizationState)
    {
        Status = StatusCancelled;
        PaymentStatus = PaymentStatusCancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(authorizationState))
        {
            FinixAuthorizationState = authorizationState;
        }
    }

    public void ApplyRefund(int totalRefundedAmountCents)
    {
        Guard.Against.Negative(totalRefundedAmountCents, nameof(totalRefundedAmountCents));

        RefundedAmountCents = totalRefundedAmountCents;
        if (CapturedAmountCents.HasValue)
        {
            PaymentStatus = RefundedAmountCents >= CapturedAmountCents.Value
                ? PaymentStatusRefunded
                : PaymentStatusPartiallyRefunded;
        }
    }

    private static int ToCents(decimal amount)
    {
        return checked((int)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }
}

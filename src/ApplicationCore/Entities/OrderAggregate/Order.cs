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
    public Guid PaymentReference { get; private set; } = Guid.NewGuid();
    public Address ShipToAddress { get; private set; }

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

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public OrderFulfilmentStatus FulfilmentStatus { get; private set; } = OrderFulfilmentStatus.Unfulfilled;
    public string? PaymentCurrency { get; private set; }
    public string? PaymentFailureMessage { get; private set; }

    public string? FinixAuthorizationId { get; private set; }
    public string? FinixAuthorizationState { get; private set; }
    public string? FinixAuthorizationFailureCode { get; private set; }
    public string? FinixAuthorizationFailureMessage { get; private set; }
    public DateTimeOffset? FinixAuthorizationExpiresAt { get; private set; }
    public bool FinixAuthorizationVoided { get; private set; }
    public string? FinixAuthorizationVoidState { get; private set; }
    public int AuthorizationAttempt { get; private set; }

    public long? AuthorizedAmountCents { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    public string? FinixCaptureTransferId { get; private set; }
    public string? FinixCaptureState { get; private set; }
    public long? CapturedAmountCents { get; private set; }
    public long? FinixFeeCents { get; private set; }
    public long? NetProceedsCents { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public long RefundedAmountCents { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public long TotalCents() => Money.ToCents(Total());

    public void RecordAuthorization(
        string authorizationId,
        string state,
        long authorizedAmountCents,
        string currency,
        int attempt,
        DateTimeOffset? expiresAt,
        string? failureCode,
        string? failureMessage)
    {
        FinixAuthorizationId = authorizationId;
        FinixAuthorizationState = state;
        AuthorizedAmountCents = authorizedAmountCents;
        PaymentCurrency = currency;
        AuthorizationAttempt = attempt;
        FinixAuthorizationExpiresAt = expiresAt;
        FinixAuthorizationFailureCode = failureCode;
        FinixAuthorizationFailureMessage = failureMessage;
        PaymentFailureMessage = failureMessage;
        FinixAuthorizationVoided = false;
        AuthorizedAt = DateTimeOffset.UtcNow;

        if (string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            PaymentStatus = OrderPaymentStatus.Authorized;
            PaymentFailureMessage = null;
        }
        else if (string.Equals(state, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            PaymentStatus = OrderPaymentStatus.Authorizing;
        }
        else
        {
            PaymentStatus = OrderPaymentStatus.Failed;
        }
    }

    public void RecordAuthorizationFailure(string? authorizationId, string state, string? failureCode, string? failureMessage)
    {
        FinixAuthorizationId = authorizationId;
        FinixAuthorizationState = state;
        FinixAuthorizationFailureCode = failureCode;
        FinixAuthorizationFailureMessage = failureMessage;
        PaymentFailureMessage = failureMessage;
        PaymentStatus = OrderPaymentStatus.Failed;
    }

    public void SetAuthorizationAttempt(int attempt)
    {
        if (attempt > AuthorizationAttempt)
        {
            AuthorizationAttempt = attempt;
        }
    }

    public void UpdateAuthorizationSnapshot(
        string state,
        bool isVoid,
        string? voidState,
        DateTimeOffset? expiresAt,
        string? failureCode,
        string? failureMessage)
    {
        FinixAuthorizationState = state;
        FinixAuthorizationVoided = isVoid ||
            string.Equals(voidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase);
        FinixAuthorizationVoidState = voidState;
        FinixAuthorizationExpiresAt = expiresAt ?? FinixAuthorizationExpiresAt;
        FinixAuthorizationFailureCode = failureCode;
        FinixAuthorizationFailureMessage = failureMessage;

        if (string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) &&
            !FinixAuthorizationVoided)
        {
            if (PaymentStatus == OrderPaymentStatus.Authorizing ||
                PaymentStatus == OrderPaymentStatus.Failed ||
                PaymentStatus == OrderPaymentStatus.AwaitingPayment)
            {
                PaymentStatus = OrderPaymentStatus.Authorized;
            }
        }
        else if (string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(state, "CANCELED", StringComparison.OrdinalIgnoreCase))
        {
            PaymentStatus = OrderPaymentStatus.Failed;
            PaymentFailureMessage = failureMessage;
        }
    }

    public void MarkFulfilled()
    {
        FulfilmentStatus = OrderFulfilmentStatus.Fulfilled;
        FulfilledAt = DateTimeOffset.UtcNow;
    }

    public void RecordCapture(
        string transferId,
        string captureState,
        long capturedAmountCents,
        long feeCents,
        long netProceedsCents)
    {
        FinixCaptureTransferId = transferId;
        FinixCaptureState = captureState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = feeCents;
        NetProceedsCents = netProceedsCents;
        CapturedAt = DateTimeOffset.UtcNow;
        PaymentStatus = RefundedAmountCents <= 0
            ? OrderPaymentStatus.Captured
            : (RefundedAmountCents >= capturedAmountCents
                ? OrderPaymentStatus.Refunded
                : OrderPaymentStatus.PartiallyRefunded);
    }

    public void UpdateCaptureState(string captureState, long? capturedAmountCents = null, long? feeCents = null)
    {
        FinixCaptureState = captureState;
        if (capturedAmountCents.HasValue)
        {
            CapturedAmountCents = capturedAmountCents.Value;
        }
        if (feeCents.HasValue)
        {
            FinixFeeCents = feeCents.Value;
            if (CapturedAmountCents.HasValue)
            {
                NetProceedsCents = CapturedAmountCents.Value - feeCents.Value;
            }
        }
    }

    public void MarkCancelled()
    {
        PaymentStatus = OrderPaymentStatus.Cancelled;
        FulfilmentStatus = OrderFulfilmentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        FinixAuthorizationVoided = true;
        FinixAuthorizationVoidState = "SUCCEEDED";
    }

    public void AddRefund(OrderRefund refund)
    {
        _refunds.Add(refund);
        RefundedAmountCents += refund.AmountCents;

        if (CapturedAmountCents.HasValue && RefundedAmountCents >= CapturedAmountCents.Value)
        {
            PaymentStatus = OrderPaymentStatus.Refunded;
        }
        else if (RefundedAmountCents > 0)
        {
            PaymentStatus = OrderPaymentStatus.PartiallyRefunded;
        }
    }

    public long RemainingRefundableCents()
    {
        var captured = CapturedAmountCents ?? 0;
        return Math.Max(0, captured - RefundedAmountCents);
    }
}

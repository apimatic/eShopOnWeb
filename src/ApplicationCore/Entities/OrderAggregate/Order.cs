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

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string? currency = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        PaymentStatus = PaymentStatus.AwaitingPayment;
        FulfillmentStatus = FulfillmentStatus.AwaitingFulfillment;
        Currency = currency;
        PaymentReference = "eshop-" + Guid.NewGuid().ToString("N");
        ConcurrencyToken = Guid.NewGuid();
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public FulfillmentStatus FulfillmentStatus { get; private set; }
    public string? Currency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public int? PaymentMethodId { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public Guid ConcurrencyToken { get; private set; }
    public string PaymentReference { get; private set; }

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

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => _refunds
        .Where(refund => refund.Status == "COMPLETED")
        .Sum(refund => refund.Amount);

    private decimal ReservedRefundAmount => _refunds
        .Where(refund => refund.Status is "COMPLETED" or "PENDING")
        .Sum(refund => refund.Amount);

    public decimal RefundableAmount => Math.Max(0m, (CapturedAmount ?? 0m) - ReservedRefundAmount);

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void RecordAuthorization(string currency, string payPalOrderId, string authorizationId,
        string status, DateTimeOffset? createdAt, DateTimeOffset? expiresAt, int? paymentMethodId)
    {
        if (PaymentStatus != PaymentStatus.AwaitingPayment)
        {
            throw new InvalidOperationException("Only an order awaiting payment can be authorized.");
        }

        Currency = currency;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentMethodId = paymentMethodId;
        PaymentStatus = status == "PENDING" ? PaymentStatus.AuthorizationPending : PaymentStatus.Authorized;
        Touch();
    }

    public void RefreshAuthorization(string status, DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        if (PaymentStatus is not (PaymentStatus.AuthorizationPending or PaymentStatus.Authorized))
        {
            throw new InvalidOperationException("Only a current authorization can be refreshed.");
        }

        AuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt ?? AuthorizationCreatedAt;
        AuthorizationExpiresAt = expiresAt ?? AuthorizationExpiresAt;
        PaymentStatus = status switch
        {
            "CREATED" => PaymentStatus.Authorized,
            "PENDING" => PaymentStatus.AuthorizationPending,
            "VOIDED" or "DENIED" => PaymentStatus.Voided,
            _ => PaymentStatus
        };
        Touch();
    }

    public void RecordReauthorization(string authorizationId, string status,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        if (PaymentStatus != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException("Only an authorized payment can be reauthorized.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount,
        decimal payPalFee, decimal netProceeds, DateTimeOffset fulfilledAt)
    {
        if (PaymentStatus is not (PaymentStatus.Authorized or PaymentStatus.CapturePending) ||
            FulfillmentStatus != FulfillmentStatus.AwaitingFulfillment)
        {
            throw new InvalidOperationException("Only an authorized, unfulfilled order can be captured.");
        }

        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetProceeds = netProceeds;
        PaymentStatus = PaymentStatus.Captured;
        FulfillmentStatus = FulfillmentStatus.Fulfilled;
        FulfilledAt = fulfilledAt;
        Touch();
    }

    public void RecordPendingCapture(string captureId, string status, decimal capturedAmount)
    {
        if (PaymentStatus != PaymentStatus.Authorized || FulfillmentStatus != FulfillmentStatus.AwaitingFulfillment)
        {
            throw new InvalidOperationException("Only an authorized, unfulfilled order can begin capture.");
        }

        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PaymentStatus = PaymentStatus.CapturePending;
        Touch();
    }

    public void Cancel(string authorizationStatus, DateTimeOffset cancelledAt)
    {
        if (FulfillmentStatus != FulfillmentStatus.AwaitingFulfillment ||
            PaymentStatus is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new InvalidOperationException("A fulfilled or captured order cannot be cancelled.");
        }

        AuthorizationStatus = authorizationStatus;
        PaymentStatus = PaymentStatus.Voided;
        FulfillmentStatus = FulfillmentStatus.Cancelled;
        CancelledAt = cancelledAt;
        Touch();
    }

    public OrderRefund AddRefund(string idempotencyKey, string providerRefundId,
        decimal amount, string status, DateTimeOffset createdAt)
    {
        if (PaymentStatus is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.RefundPending))
        {
            throw new InvalidOperationException("Only a captured payment can be refunded.");
        }

        if (amount <= 0m || amount > RefundableAmount)
        {
            throw new InvalidOperationException("Refund amount exceeds the remaining captured amount.");
        }

        var refund = new OrderRefund(idempotencyKey, providerRefundId, amount, status, createdAt);
        _refunds.Add(refund);
        RecalculateRefundState();
        Touch();
        return refund;
    }

    public void UpdateRefundStatus(string providerRefundId, string status)
    {
        var refund = _refunds.SingleOrDefault(candidate => candidate.ProviderRefundId == providerRefundId) ??
                     throw new InvalidOperationException("The refund does not belong to this order.");
        refund.UpdateStatus(status);
        RecalculateRefundState();
        Touch();
    }

    private void RecalculateRefundState()
    {
        if (_refunds.Any(refund => refund.Status == "PENDING"))
        {
            PaymentStatus = PaymentStatus.RefundPending;
            CaptureStatus = "REFUND_PENDING";
            return;
        }

        PaymentStatus = RefundedAmount switch
        {
            0m => PaymentStatus.Captured,
            var amount when amount >= (CapturedAmount ?? 0m) => PaymentStatus.Refunded,
            _ => PaymentStatus.PartiallyRefunded
        };
        CaptureStatus = PaymentStatus switch
        {
            PaymentStatus.Refunded => "REFUNDED",
            PaymentStatus.PartiallyRefunded => "PARTIALLY_REFUNDED",
            _ => "COMPLETED"
        };
    }

    private void Touch() => ConcurrencyToken = Guid.NewGuid();
}

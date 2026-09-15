using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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
        Status = OrderStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public string? Currency { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? PayPalAuthorizationCreated { get; private set; }
    public DateTimeOffset? PayPalAuthorizationExpiration { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx 
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public decimal RefundedTotal() => _refunds.Sum(r => r.Amount);

    public decimal RefundableRemaining()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.Ordinal);

    public void SetCurrency(string currency)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Currency = currency;
    }

    public void AttachPayPalOrder(string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        EnsureNotCancelled();
        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException("This order has already been fulfilled and cannot be authorized again.");
        }

        PayPalOrderId = payPalOrderId;
    }

    public void RecordAuthorization(
        string authorizationId,
        string status,
        DateTimeOffset? created,
        DateTimeOffset? expiration,
        string currency)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        EnsureNotCancelled();
        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException("This order has already been fulfilled and cannot be authorized again.");
        }

        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        PayPalAuthorizationCreated = created;
        PayPalAuthorizationExpiration = expiration;
        Currency = currency;
        Status = OrderStatus.Authorized;
    }

    public void ReplaceAuthorization(
        string authorizationId,
        string status,
        DateTimeOffset? created,
        DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != OrderStatus.Authorized)
        {
            throw new PaymentConflictException("Only an authorized order can have its payment hold renewed.");
        }

        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        PayPalAuthorizationCreated = created;
        PayPalAuthorizationExpiration = expiration;
    }

    public void RecordCapture(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal? payPalFee,
        decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        PayPalAuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
    }

    public void Cancel(string? voidedAuthorizationStatus = null)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException("A fulfilled order cannot be cancelled; issue a refund instead.");
        }

        if (!string.IsNullOrEmpty(voidedAuthorizationStatus))
        {
            PayPalAuthorizationStatus = voidedAuthorizationStatus;
        }

        Status = OrderStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string payPalRefundId, string payPalRefundStatus, string idempotencyKey, decimal amount)
    {
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
        {
            throw new PaymentConflictException("Refunds can only be issued after the order has been fulfilled.");
        }

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var remaining = RefundableRemaining();
        if (amount > remaining)
        {
            throw new PaymentConflictException($"Refund of {amount} exceeds the remaining captured amount of {remaining}.");
        }

        var refund = new OrderRefund(payPalRefundId, payPalRefundStatus, idempotencyKey, amount);
        _refunds.Add(refund);

        var leftover = RefundableRemaining();
        Status = leftover == 0 ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        if (!string.IsNullOrEmpty(PayPalCaptureStatus))
        {
            PayPalCaptureStatus = leftover == 0 ? "REFUNDED" : "PARTIALLY_REFUNDED";
        }

        return refund;
    }

    public void EnsureOwnedBy(string buyerId)
    {
        if (!BelongsTo(buyerId))
        {
            throw new PaymentForbiddenException("This order does not belong to the signed-in shopper.");
        }
    }

    public void EnsureCanPay()
    {
        EnsureNotCancelled();
        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException("This order has already been fulfilled.");
        }
    }

    public void EnsureCanFulfil()
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new PaymentConflictException("A cancelled order cannot be fulfilled.");
        }

        if (Status is OrderStatus.AwaitingPayment)
        {
            throw new PaymentConflictException("This order has no payment hold to capture. The shopper must pay first.");
        }
    }

    public void EnsureCanCancel()
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException("A fulfilled order cannot be cancelled; issue a refund instead.");
        }
    }

    private void EnsureNotCancelled()
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new PaymentConflictException("This order has been cancelled.");
        }
    }
}

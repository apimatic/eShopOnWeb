using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
    public static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);
    public static readonly TimeSpan AuthorizationMaxLifetime = TimeSpan.FromDays(29);

    #pragma warning disable CS8618 // Required by Entity Framework
    private Order() {}
    #pragma warning restore CS8618

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
        : this(buyerId, shipToAddress, items, OrderLifecycleStatus.Placed)
    {
    }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, OrderLifecycleStatus status)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Status = status;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderLifecycleStatus Status { get; private set; } = OrderLifecycleStatus.Placed;
    public OrderPayment? Payment { get; private set; }

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
        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    public void EnsureOwnedBy(string buyerId)
    {
        if (!BelongsTo(buyerId))
        {
            throw new ForbiddenAccessException("This order does not belong to the caller.");
        }
    }

    public bool HasActiveAuthorization =>
        Status == OrderLifecycleStatus.Authorized
        && Payment?.AuthorizationId is not null
        && !string.Equals(Payment.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(Payment.AuthorizationStatus, "CAPTURED", StringComparison.OrdinalIgnoreCase);

    public bool AuthorizationHonorPeriodElapsed(DateTimeOffset utcNow)
    {
        var start = Payment?.AuthorizationCreateTime ?? Payment?.OriginalAuthorizationTime;
        if (start is null)
        {
            return false;
        }

        return utcNow - start.Value >= AuthorizationHonorPeriod;
    }

    public bool AuthorizationCanNoLongerBeRenewed(DateTimeOffset utcNow)
    {
        var original = Payment?.OriginalAuthorizationTime;
        if (original is null)
        {
            return false;
        }

        return utcNow - original.Value >= AuthorizationMaxLifetime;
    }

    public void BeginPayment(string paypalOrderId, string currency, string? invoiceId = null)
    {
        EnsureCanPay();
        Payment ??= new OrderPayment(paypalOrderId, currency, invoiceId);
    }

    public void RecordAuthorization(
        string paypalOrderId,
        string authorizationId,
        string status,
        string currency,
        DateTimeOffset? createTime,
        DateTimeOffset? expirationTime,
        string? invoiceId = null)
    {
        EnsureCanPay();
        Payment ??= new OrderPayment(paypalOrderId, currency, invoiceId);
        if (!string.IsNullOrWhiteSpace(invoiceId))
        {
            Payment.SetInvoiceId(invoiceId);
        }
        Payment.RecordAuthorization(authorizationId, status, createTime, expirationTime);
        Status = OrderLifecycleStatus.Authorized;
    }

    public void RecordReauthorization(
        string authorizationId,
        string status,
        DateTimeOffset? createTime,
        DateTimeOffset? expirationTime)
    {
        if (Payment is null)
        {
            throw new OrderPaymentException("This order has no authorization to renew.");
        }

        Payment.RecordReauthorization(authorizationId, status, createTime, expirationTime);
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal paypalFee, decimal netAmount)
    {
        if (Status == OrderLifecycleStatus.Fulfilled
            || Status == OrderLifecycleStatus.PartiallyRefunded
            || Status == OrderLifecycleStatus.Refunded)
        {
            return;
        }

        if (Status != OrderLifecycleStatus.Authorized)
        {
            throw new OrderPaymentException("An order can only be fulfilled after its payment has been authorized.");
        }

        if (Payment is null)
        {
            throw new OrderPaymentException("This order has no authorized payment to capture.");
        }

        Payment.RecordCapture(captureId, status, capturedAmount, paypalFee, netAmount);
        Status = OrderLifecycleStatus.Fulfilled;
    }

    public void RecordVoid(string status)
    {
        if (Status == OrderLifecycleStatus.Cancelled)
        {
            return;
        }

        if (Status is OrderLifecycleStatus.Fulfilled or OrderLifecycleStatus.PartiallyRefunded or OrderLifecycleStatus.Refunded)
        {
            throw new OrderPaymentException("A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        Payment?.RecordVoid(status);
        Status = OrderLifecycleStatus.Cancelled;
    }

    public void CancelWithoutPayment()
    {
        if (Status == OrderLifecycleStatus.Cancelled)
        {
            return;
        }

        if (Status != OrderLifecycleStatus.AwaitingPayment && Status != OrderLifecycleStatus.Placed)
        {
            throw new OrderPaymentException("Only unpaid orders can be cancelled without releasing a PayPal hold.");
        }

        Status = OrderLifecycleStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string paypalRefundId, string status, decimal amount, string idempotencyKey)
    {
        if (Status is not (OrderLifecycleStatus.Fulfilled or OrderLifecycleStatus.PartiallyRefunded or OrderLifecycleStatus.Refunded))
        {
            throw new OrderPaymentException("Refunds can only be issued after the order has been fulfilled.");
        }

        if (Payment is null || string.IsNullOrEmpty(Payment.CaptureId))
        {
            throw new OrderPaymentException("This order has no captured payment to refund.");
        }

        var existing = Payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var refundAmount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (refundAmount <= 0)
        {
            throw new OrderPaymentException("Refund amount must be greater than zero.", 400);
        }

        if (refundAmount > Payment.RefundableRemaining)
        {
            throw new OrderPaymentException(
                $"Refund of {refundAmount:0.00} exceeds the remaining captured amount of {Payment.RefundableRemaining:0.00}.");
        }

        var refund = Payment.RecordRefund(paypalRefundId, status, refundAmount, idempotencyKey);
        Status = Payment.RefundableRemaining == 0m
            ? OrderLifecycleStatus.Refunded
            : OrderLifecycleStatus.PartiallyRefunded;
        return refund;
    }

    public void EnsureCanPay()
    {
        if (Status == OrderLifecycleStatus.Authorized && HasActiveAuthorization)
        {
            return;
        }

        if (Status != OrderLifecycleStatus.AwaitingPayment && Status != OrderLifecycleStatus.Placed)
        {
            throw new OrderPaymentException($"Order {Id} cannot be paid while it is {Status}.");
        }
    }

    public void EnsureCanFulfil()
    {
        if (Status is OrderLifecycleStatus.Fulfilled or OrderLifecycleStatus.PartiallyRefunded or OrderLifecycleStatus.Refunded)
        {
            return;
        }

        if (Status != OrderLifecycleStatus.Authorized)
        {
            throw new OrderPaymentException("An order can only be fulfilled after its payment has been authorized.");
        }
    }

    public void EnsureCanCancel()
    {
        if (Status == OrderLifecycleStatus.Cancelled)
        {
            return;
        }

        if (Status is OrderLifecycleStatus.Fulfilled or OrderLifecycleStatus.PartiallyRefunded or OrderLifecycleStatus.Refunded)
        {
            throw new OrderPaymentException("A fulfilled order cannot be cancelled. Issue a refund instead.");
        }
    }

    public IReadOnlyList<string> PayPalIdentifiers()
    {
        if (Payment is null)
        {
            return Array.Empty<string>();
        }

        var ids = new List<string>();
        AddIfPresent(ids, Payment.PayPalOrderId);
        AddIfPresent(ids, Payment.InvoiceId);
        AddIfPresent(ids, Payment.OriginalAuthorizationId);
        AddIfPresent(ids, Payment.AuthorizationId);
        AddIfPresent(ids, Payment.CaptureId);
        foreach (var refund in Payment.Refunds)
        {
            AddIfPresent(ids, refund.PayPalRefundId);
        }

        return ids;
    }

    private static void AddIfPresent(List<string> ids, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !ids.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            ids.Add(value);
        }
    }
}

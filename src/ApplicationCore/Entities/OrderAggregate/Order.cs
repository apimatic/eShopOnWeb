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
        Status = OrderPaymentStatus.AwaitingPayment;
        PaymentOperationKey = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderPaymentStatus Status { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string PaymentOperationKey { get; private set; } = string.Empty;

    public string? PaypalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGross { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? PayIdempotencyKey { get; private set; }
    public string? CaptureIdempotencyKey { get; private set; }
    public string? VoidIdempotencyKey { get; private set; }

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

    public decimal RefundedTotal() =>
        _refunds.Where(r => r.CountsAgainstCaptured).Sum(r => r.Amount);

    public decimal RemainingRefundable()
    {
        var captured = CapturedGross ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public void RecordAuthorization(
        string paypalOrderId,
        string authorizationId,
        string? authorizationStatus,
        DateTimeOffset? expirationTime,
        string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PaypalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpirationTime = expirationTime;
        PayIdempotencyKey = idempotencyKey;
        Status = OrderPaymentStatus.Authorized;
    }

    public void ReplaceAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expirationTime)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpirationTime = expirationTime;
    }

    public void RecordCapture(
        string captureId,
        string? captureStatus,
        decimal? capturedGross,
        decimal? paypalFee,
        decimal? netAmount,
        string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGross = capturedGross;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        CaptureIdempotencyKey = idempotencyKey;
        AuthorizationStatus = "CAPTURED";
        Status = OrderPaymentStatus.Fulfilled;
    }

    public void RecordVoid(string? authorizationStatus, string idempotencyKey)
    {
        AuthorizationStatus = authorizationStatus ?? "VOIDED";
        VoidIdempotencyKey = idempotencyKey;
        Status = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string paypalRefundId, string? status, decimal amount, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(paypalRefundId, nameof(paypalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        if (amount <= 0)
        {
            throw new OrderPaymentException("Refund amount must be greater than zero.", 400);
        }

        var remaining = RemainingRefundable();
        if (amount > remaining)
        {
            throw new OrderPaymentException(
                $"Refund of {amount} exceeds remaining refundable amount {remaining}.", 400);
        }

        var refund = new OrderRefund(paypalRefundId, status ?? "COMPLETED", amount, idempotencyKey);
        _refunds.Add(refund);

        var leftover = RemainingRefundable();
        Status = leftover <= 0 ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded;
        CaptureStatus = leftover <= 0 ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return refund;
    }

    public void EnsureOwnedBy(string buyerId)
    {
        if (!string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new OrderPaymentException("The requested order was not found.", 404);
        }
    }

    public void EnsureCanPay()
    {
        if (Status == OrderPaymentStatus.Authorized || Status == OrderPaymentStatus.Fulfilled
            || Status == OrderPaymentStatus.PartiallyRefunded || Status == OrderPaymentStatus.Refunded)
        {
            return;
        }

        if (Status == OrderPaymentStatus.Cancelled)
        {
            throw new OrderPaymentException("A cancelled order cannot be paid.", 409);
        }
    }

    public bool AlreadyAuthorized =>
        Status is OrderPaymentStatus.Authorized or OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded
        && !string.IsNullOrEmpty(AuthorizationId);

    public bool AlreadyCaptured =>
        Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded
        && !string.IsNullOrEmpty(CaptureId);

    public bool AlreadyCancelled => Status == OrderPaymentStatus.Cancelled;

    public void EnsureCanFulfil()
    {
        if (AlreadyCaptured)
        {
            return;
        }

        if (Status == OrderPaymentStatus.Cancelled)
        {
            throw new OrderPaymentException("A cancelled order cannot be fulfilled.", 409);
        }

        if (Status != OrderPaymentStatus.Authorized || string.IsNullOrEmpty(AuthorizationId))
        {
            throw new OrderPaymentException("The order must be authorized before it can be fulfilled.", 409);
        }
    }

    public void EnsureCanCancel()
    {
        if (AlreadyCancelled)
        {
            return;
        }

        if (AlreadyCaptured)
        {
            throw new OrderPaymentException("A fulfilled order cannot be cancelled. Issue a refund instead.", 409);
        }

        if (Status != OrderPaymentStatus.Authorized || string.IsNullOrEmpty(AuthorizationId))
        {
            throw new OrderPaymentException("Only an authorized, unfulfilled order can be cancelled.", 409);
        }
    }

    public void EnsureCanRefund()
    {
        if (!AlreadyCaptured)
        {
            throw new OrderPaymentException("Only a fulfilled order can be refunded.", 409);
        }

        if (RemainingRefundable() <= 0)
        {
            throw new OrderPaymentException("This order has already been refunded in full.", 409);
        }
    }
}

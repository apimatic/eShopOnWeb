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
        PaymentNonce = Guid.NewGuid();
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? Currency { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public int PaymentAttempt { get; private set; }
    public Guid PaymentNonce { get; private set; }

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

    public decimal RemainingRefundableAmount()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public string NextPayRequestId()
    {
        PaymentAttempt++;
        return $"eshop-auth-{PaymentNonce:N}-a{PaymentAttempt}";
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        string currency,
        DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        if (Status == OrderStatus.Cancelled)
        {
            throw new PaymentConflictException("This order has been cancelled and cannot be paid.");
        }

        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException("This order has already been fulfilled.");
        }

        if (Status == OrderStatus.Authorized &&
            AuthorizationId == authorizationId)
        {
            AuthorizationStatus = authorizationStatus;
            AuthorizationExpiration = expiration;
            return;
        }

        if (Status == OrderStatus.Authorized)
        {
            throw new PaymentConflictException("This order has already been authorized.");
        }

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        AuthorizedAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Authorized;
    }

    public void ReplaceAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != OrderStatus.Authorized)
        {
            throw new PaymentConflictException("Only an authorized order can have its hold renewed.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? payPalFee,
        decimal? netAmount,
        string currency)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));
        Guard.Against.NegativeOrZero(capturedAmount, nameof(capturedAmount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        if (Status is OrderStatus.Cancelled)
        {
            throw new PaymentConflictException("A cancelled order cannot be fulfilled.");
        }

        if (Status is OrderStatus.AwaitingPayment)
        {
            throw new PaymentConflictException("This order has not been paid. Authorize payment before fulfilment.");
        }

        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            if (CaptureId == captureId)
            {
                CaptureStatus = captureStatus;
                CapturedAmount = capturedAmount;
                PayPalFee = payPalFee;
                NetAmount = netAmount;
                return;
            }

            throw new PaymentConflictException("This order has already been fulfilled.");
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Currency = currency;
        FulfilledAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
    }

    public void RecordCancellation()
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException("This order has already been fulfilled. Issue a refund instead of cancelling.");
        }

        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        Status = OrderStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrEmpty(AuthorizationStatus) &&
            !string.Equals(AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            AuthorizationStatus = "VOIDED";
        }
    }

    public OrderRefund RecordRefund(
        string payPalRefundId,
        string idempotencyKey,
        decimal amount,
        string currency,
        string status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(status, nameof(status));

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
        {
            throw new PaymentConflictException("Refunds are only available after the order has been fulfilled.");
        }

        var remaining = RemainingRefundableAmount();
        if (amount > remaining)
        {
            throw new PaymentValidationException(
                $"Refund of {amount.ToString("0.00")} exceeds the remaining refundable amount of {remaining.ToString("0.00")}.");
        }

        var refund = new OrderRefund(payPalRefundId, idempotencyKey, amount, currency, status);
        _refunds.Add(refund);

        Status = RemainingRefundableAmount() == 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        CaptureStatus = Status == OrderStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return refund;
    }
}

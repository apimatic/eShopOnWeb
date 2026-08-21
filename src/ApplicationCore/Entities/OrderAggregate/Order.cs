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

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string? currency = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Status = OrderStatus.AwaitingPayment;
        Currency = currency;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? Currency { get; private set; }
    public int PaymentAttempt { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalInvoiceId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationTime { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }

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
    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx 
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    public decimal RefundedTotal()
    {
        var total = 0m;
        foreach (var refund in _refunds)
        {
            total += refund.Amount;
        }
        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        return decimal.Round(captured - RefundedTotal(), 2, MidpointRounding.AwayFromZero);
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
    }

    public string NextPayRequestId()
    {
        PaymentAttempt++;
        return $"eshop-order-{Id}-auth-{PaymentAttempt}-{Guid.NewGuid():N}";
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expirationTime,
        string currency,
        string? invoiceId = null)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        if (HasSuccessfulAuthorization())
        {
            return;
        }

        EnsureStatus(OrderStatus.AwaitingPayment, "paid");

        PayPalOrderId = payPalOrderId;
        PayPalInvoiceId = invoiceId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationTime = DateTimeOffset.UtcNow;
        AuthorizationExpirationTime = expirationTime;
        Currency = currency;
        Status = OrderStatus.Authorized;
    }

    public void ReplaceAuthorization(
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expirationTime)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        if (Status != OrderStatus.Authorized)
        {
            throw new CheckoutException(409, $"Order {Id} does not have a renewable payment hold.", "INVALID_ORDER_STATE");
        }

        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationTime = DateTimeOffset.UtcNow;
        AuthorizationExpirationTime = expirationTime;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));

        if (HasSuccessfulCapture())
        {
            return;
        }

        EnsureStatus(OrderStatus.Authorized, "fulfilled");

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = decimal.Round(capturedAmount, 2, MidpointRounding.AwayFromZero);
        PayPalFee = paypalFee.HasValue ? decimal.Round(paypalFee.Value, 2, MidpointRounding.AwayFromZero) : null;
        NetAmount = netAmount.HasValue ? decimal.Round(netAmount.Value, 2, MidpointRounding.AwayFromZero) : null;
        Status = OrderStatus.Fulfilled;
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new CheckoutException(409, $"Order {Id} has already been fulfilled and cannot be cancelled. Issue a refund instead.", "INVALID_ORDER_STATE");
        }

        if (Status is not OrderStatus.AwaitingPayment and not OrderStatus.Authorized)
        {
            throw new CheckoutException(409, $"Order {Id} cannot be cancelled in state {Status}.", "INVALID_ORDER_STATE");
        }

        if (!string.IsNullOrEmpty(PayPalAuthorizationId))
        {
            PayPalAuthorizationStatus = "VOIDED";
        }

        Status = OrderStatus.Cancelled;
    }

    public PaymentRefund RecordRefund(string payPalRefundId, string status, decimal amount, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded and not OrderStatus.Refunded)
        {
            throw new CheckoutException(409, $"Order {Id} can only be refunded after fulfilment.", "INVALID_ORDER_STATE");
        }

        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (rounded <= 0)
        {
            throw new CheckoutException(400, "Refund amount must be greater than zero.", "INVALID_REFUND_AMOUNT");
        }

        var remaining = RemainingRefundable();
        if (rounded > remaining)
        {
            throw new CheckoutException(409,
                $"Refund of {rounded} exceeds the remaining captured amount of {remaining}.",
                "REFUND_EXCEEDS_CAPTURE");
        }

        var refund = new PaymentRefund(Id, payPalRefundId, status, rounded, idempotencyKey);
        _refunds.Add(refund);

        Status = RemainingRefundable() == 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }

    public bool HasSuccessfulAuthorization() =>
        Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded
        && !string.IsNullOrEmpty(PayPalAuthorizationId);

    public bool HasSuccessfulCapture() =>
        Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded
        && !string.IsNullOrEmpty(PayPalCaptureId);

    public bool IsAuthorizationStale(DateTimeOffset utcNow)
    {
        if (string.IsNullOrEmpty(PayPalAuthorizationId) || Status != OrderStatus.Authorized)
        {
            return false;
        }

        if (string.Equals(PayPalAuthorizationStatus, "EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (AuthorizationExpirationTime.HasValue && AuthorizationExpirationTime.Value <= utcNow)
        {
            return true;
        }

        // PayPal honor period is 3 days; after that the hold should be renewed before capture.
        if (AuthorizationTime.HasValue && AuthorizationTime.Value.AddDays(3) <= utcNow)
        {
            return true;
        }

        return false;
    }

    private void EnsureStatus(OrderStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new CheckoutException(409, $"Order {Id} cannot be {action} in state {Status}.", "INVALID_ORDER_STATE");
        }
    }
}

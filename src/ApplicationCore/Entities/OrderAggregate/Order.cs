using System;
using System.Collections.Generic;
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
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
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
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx 
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    public string PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? AuthorizationExpirationTime { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    private readonly List<OrderRefund> _refunds = new();
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

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    public bool HasActiveAuthorization() =>
        !string.IsNullOrEmpty(AuthorizationId)
        && (PaymentStatus == OrderPaymentStatus.Authorized
            || string.Equals(AuthorizationStatus, "CREATED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(AuthorizationStatus, "PENDING", StringComparison.OrdinalIgnoreCase));

    public bool IsCaptured() =>
        PaymentStatus is OrderPaymentStatus.Captured
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded
        || !string.IsNullOrEmpty(CaptureId);

    public bool IsCancelled() => PaymentStatus == OrderPaymentStatus.Cancelled;

    public bool AuthorizationLooksExpired(DateTimeOffset utcNow)
    {
        if (string.IsNullOrEmpty(AuthorizationExpirationTime))
            return false;
        return DateTimeOffset.TryParse(AuthorizationExpirationTime, out var expires)
            && expires <= utcNow;
    }

    public void SetCurrency(string currency)
    {
        Currency = currency;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        foreach (var refund in _refunds)
        {
            if (string.Equals(refund.IdempotencyKey, idempotencyKey, StringComparison.Ordinal))
                return refund;
        }
        return null;
    }

    public decimal CompletedRefundTotal()
    {
        var total = 0m;
        foreach (var refund in _refunds)
        {
            if (refund.IsCompleted())
                total += refund.Amount;
        }
        return total;
    }

    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - CompletedRefundTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        string? expirationTime,
        string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        if (IsCancelled())
            throw new CheckoutException(409, "This order has been cancelled.");
        if (IsCaptured())
            throw new CheckoutException(409, "This order has already been captured.");

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpirationTime = expirationTime;
        Currency = currency;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void ReplaceAuthorization(string authorizationId, string authorizationStatus, string? expirationTime)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
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

        if (IsCancelled())
            throw new CheckoutException(409, "This order has been cancelled.");

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        PaymentStatus = OrderPaymentStatus.Captured;
    }

    public void RecordVoid(string? authorizationStatus)
    {
        if (IsCaptured())
            throw new CheckoutException(409, "A captured order cannot be cancelled; refund it instead.");

        AuthorizationStatus = string.IsNullOrEmpty(authorizationStatus) ? "VOIDED" : authorizationStatus;
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public void CancelWithoutAuthorization()
    {
        if (IsCaptured())
            throw new CheckoutException(409, "A captured order cannot be cancelled; refund it instead.");
        if (HasActiveAuthorization())
            throw new CheckoutException(409, "This order has an authorization; void the hold before cancelling.");

        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string payPalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        if (!IsCaptured())
            throw new CheckoutException(409, "Refunds are only allowed after the order has been fulfilled.");
        if (PaymentStatus == OrderPaymentStatus.Refunded)
            throw new CheckoutException(409, "This order has already been refunded in full.");

        var remaining = RemainingRefundable();
        if (amount > remaining)
            throw new CheckoutException(409, $"Refund of {amount} exceeds remaining refundable amount {remaining}.");

        var refund = new OrderRefund(payPalRefundId, status, amount, currency, idempotencyKey);
        _refunds.Add(refund);

        var leftover = RemainingRefundable();
        PaymentStatus = leftover <= 0m ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded;
        if (leftover <= 0m)
            CaptureStatus = "REFUNDED";
        else
            CaptureStatus = "PARTIALLY_REFUNDED";

        return refund;
    }
}

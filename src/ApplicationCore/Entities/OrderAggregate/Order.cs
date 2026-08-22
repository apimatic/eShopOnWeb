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
    #pragma warning restore CS8618

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

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string? Currency { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? OriginalAuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }
    public int PayAttemptCount { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public string PaymentCorrelationId { get; private set; } = Guid.NewGuid().ToString("N");

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
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

    public string InvoiceId => $"ESHOP-{Id}-{PaymentCorrelationId}";

    public string NextAuthorizeRequestId() => $"eshop-pay-{PaymentCorrelationId}-{PayAttemptCount}";

    public string CaptureRequestId() => $"eshop-capture-{PaymentCorrelationId}";

    public string VoidRequestId() => $"eshop-void-{PaymentCorrelationId}";

    public void RecordFailedPayAttempt()
    {
        PayAttemptCount++;
    }

    public void MarkAuthorized(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        string currency,
        DateTimeOffset? expirationTime)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        if (PaymentStatus is OrderPaymentStatus.Authorized
            or OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            return;
        }

        if (PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException("This order was cancelled and cannot be paid.", 409);
        }

        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        Currency = currency;
        var now = DateTimeOffset.UtcNow;
        OriginalAuthorizedAt ??= now;
        AuthorizedAt = now;
        AuthorizationExpirationTime = expirationTime;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void ApplyReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expirationTime)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizedAt = DateTimeOffset.UtcNow;
        AuthorizationExpirationTime = expirationTime;
    }

    public void UpdateAuthorizationStatus(string status)
    {
        PayPalAuthorizationStatus = status;
    }

    public void MarkFulfilled(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal paypalFee,
        decimal netProceeds)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        if (PaymentStatus is OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            return;
        }

        if (PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be fulfilled.", 409);
        }

        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentException("The order must be authorized before it can be fulfilled.", 409);
        }

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetProceeds = netProceeds;
        CapturedAt = DateTimeOffset.UtcNow;
        PayPalAuthorizationStatus = "CAPTURED";
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return;
        }

        if (PaymentStatus is OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            throw new PaymentException("A fulfilled order cannot be cancelled. Issue a refund instead.", 409);
        }

        PaymentStatus = OrderPaymentStatus.Cancelled;
        PayPalAuthorizationStatus = string.IsNullOrEmpty(PayPalAuthorizationId) ? PayPalAuthorizationStatus : "VOIDED";
    }

    public OrderRefund AddRefund(string payPalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded))
        {
            throw new PaymentException("Only a fulfilled order can be refunded.", 409);
        }

        if (PaymentStatus == OrderPaymentStatus.Refunded)
        {
            throw new PaymentException("This order has already been fully refunded.", 409);
        }

        var remaining = RemainingRefundable();
        if (amount <= 0)
        {
            throw new PaymentException("Refund amount must be greater than zero.", 400);
        }

        if (amount > remaining)
        {
            throw new PaymentException(
                $"Refund of {amount:0.00} exceeds the remaining refundable amount of {remaining:0.00} {currency}.",
                400);
        }

        var refund = new OrderRefund(payPalRefundId, status, amount, currency, idempotencyKey);
        _refunds.Add(refund);

        var leftover = RemainingRefundable();
        PaymentStatus = leftover == 0 ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded;
        return refund;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var refunded = _refunds.Where(r => r.CountsAgainstCapturedAmount).Sum(r => r.Amount);
        var remaining = captured - refunded;
        return remaining < 0 ? 0 : remaining;
    }

    public bool AuthorizationHonorPeriodElapsed(DateTimeOffset utcNow)
    {
        if (AuthorizedAt == null)
        {
            return false;
        }

        return utcNow >= AuthorizedAt.Value.AddDays(3);
    }

    public bool AuthorizationWindowClosed(DateTimeOffset utcNow)
    {
        if (OriginalAuthorizedAt == null)
        {
            return false;
        }

        return utcNow >= OriginalAuthorizedAt.Value.AddDays(29);
    }

    public bool OwnedBy(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);
}

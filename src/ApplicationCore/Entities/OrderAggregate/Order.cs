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
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
        PaymentReference = Guid.NewGuid().ToString("N");
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
    public string PaymentReference { get; private set; } = string.Empty;

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? Currency { get; private set; }

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

    public decimal RefundedTotal() => _refunds.Sum(r => r.Amount);

    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string key) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, key, StringComparison.Ordinal));

    public OrderRefund? FindRefundByPayPalId(string payPalRefundId) =>
        _refunds.FirstOrDefault(r => string.Equals(r.PayPalRefundId, payPalRefundId, StringComparison.Ordinal));

    public bool AuthorizationIsStale(DateTimeOffset utcNow)
    {
        if (PaymentStatus != OrderPaymentStatus.Authorized || string.IsNullOrEmpty(AuthorizationId))
            return false;
        if (AuthorizationExpiration.HasValue && AuthorizationExpiration.Value <= utcNow)
            return true;
        if (AuthorizedAt.HasValue && utcNow - AuthorizedAt.Value > TimeSpan.FromDays(3))
            return true;
        return false;
    }

    public bool AuthorizationCanNoLongerBeRenewed(DateTimeOffset utcNow)
    {
        return AuthorizedAt.HasValue && utcNow - AuthorizedAt.Value >= TimeSpan.FromDays(29);
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiration,
        string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        AuthorizedAt = DateTimeOffset.UtcNow;
        Currency = currency;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void ReplaceAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void RecordVoid(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public void CancelUnpaid()
    {
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new OrderRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        var remaining = RemainingRefundable();
        PaymentStatus = remaining <= 0m ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded;
        if (!string.IsNullOrEmpty(CaptureStatus))
        {
            CaptureStatus = remaining <= 0m ? "REFUNDED" : "PARTIALLY_REFUNDED";
        }
        return refund;
    }
}

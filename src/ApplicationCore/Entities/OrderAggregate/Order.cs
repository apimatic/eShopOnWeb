using System;
using System.Collections.Generic;
using System.Globalization;
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
        PaymentIdempotencyKey = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string PaymentIdempotencyKey { get; private set; } = string.Empty;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
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

    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var refunded = _refunds.Where(r => r.CountsAgainstRemaining).Sum(r => r.Amount);
        return captured - refunded;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void RecordAuthorization(string paypalOrderId, string authorizationId, string status, decimal amount, DateTimeOffset? expiresAt)
    {
        PayPalOrderId = paypalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = status;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        PayPalAuthorizationStatus = "CAPTURED";
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void RecordVoid()
    {
        PayPalAuthorizationStatus = "VOIDED";
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public void MarkCancelledWithoutPayment()
    {
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string paypalRefundId, string status, decimal amount, string idempotencyKey)
    {
        var refund = new OrderRefund(paypalRefundId, status, amount, idempotencyKey);
        _refunds.Add(refund);
        var remaining = RemainingRefundable();
        PaymentStatus = remaining <= 0m ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded;
        return refund;
    }

    public static DateTimeOffset? ParseExpiration(string? expirationTime)
    {
        if (string.IsNullOrWhiteSpace(expirationTime)) return null;
        if (DateTimeOffset.TryParse(expirationTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed;
        return null;
    }
}

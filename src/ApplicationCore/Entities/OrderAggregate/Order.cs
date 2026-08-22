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
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderPaymentStatus PaymentStatus { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? Currency { get; private set; }

    public string? AuthorizeIdempotencyKey { get; private set; }
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

    public decimal RefundedTotal()
    {
        return _refunds.Where(r => r.CountsAgainstCapturedTotal).Sum(r => r.Amount);
    }

    public decimal RefundableRemaining()
    {
        if (PaymentStatus is not OrderPaymentStatus.Fulfilled and not OrderPaymentStatus.PartiallyRefunded)
        {
            return 0m;
        }

        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0m ? 0m : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public void RecordPayPalOrder(string payPalOrderId, string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        PayPalOrderId = payPalOrderId;
        Currency = currency;
    }

    public void MarkAuthorized(
        string payPalOrderId,
        string authorizationId,
        string? authorizationStatus,
        DateTimeOffset? expiration,
        string currency,
        string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        if (PaymentStatus is OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.Cancelled
            or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Order {Id} cannot be authorized from status {PaymentStatus}.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        AuthorizedAt = DateTimeOffset.UtcNow;
        Currency = currency;
        AuthorizeIdempotencyKey = idempotencyKey;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void ReplaceAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {Id} has no authorization to replace.");
        }

        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
    }

    public void MarkFulfilled(
        string captureId,
        string? captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled from status {PaymentStatus}.");
        }

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        CaptureIdempotencyKey = idempotencyKey;
        PayPalAuthorizationStatus = "CAPTURED";
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void MarkCancelled(string idempotencyKey)
    {
        if (PaymentStatus != OrderPaymentStatus.Authorized && PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Order {Id} cannot be cancelled from status {PaymentStatus}.");
        }

        VoidIdempotencyKey = idempotencyKey;
        PayPalAuthorizationStatus = "VOIDED";
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status)
    {
        if (PaymentStatus is not OrderPaymentStatus.Fulfilled and not OrderPaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Order {Id} cannot be refunded from status {PaymentStatus}.");
        }

        var refund = new OrderRefund(payPalRefundId, idempotencyKey, amount, status);
        _refunds.Add(refund);

        var remaining = RefundableRemaining();
        if (remaining <= 0m)
        {
            PaymentStatus = OrderPaymentStatus.Refunded;
            PayPalCaptureStatus = "REFUNDED";
        }
        else
        {
            PaymentStatus = OrderPaymentStatus.PartiallyRefunded;
            PayPalCaptureStatus = "PARTIALLY_REFUNDED";
        }

        return refund;
    }
}

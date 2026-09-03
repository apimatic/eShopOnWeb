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
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
        PayPalInvoiceId = $"eshop-{Guid.NewGuid():N}";
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderPaymentStatus PaymentStatus { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalInvoiceId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? Currency { get; private set; }

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
        _refunds.Where(r => r.CountsAgainstCapturedAmount).Sum(r => r.Amount);

    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void EnsureOwnedBy(string buyerId)
    {
        if (!string.Equals(BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new ForbiddenResourceException("This order does not belong to the caller.");
        }
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiration,
        string currency)
    {
        if (PaymentStatus is OrderPaymentStatus.Authorized
            or OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            return;
        }

        if (PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new InvalidOrderStateException("A cancelled order cannot be authorized.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        Currency = currency;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void RefreshAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiration)
    {
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        string currency)
    {
        if (PaymentStatus is OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            return;
        }

        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new InvalidOrderStateException("Only an authorized order can be fulfilled.");
        }

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        Currency = currency;
        PayPalAuthorizationStatus = "CAPTURED";
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void Cancel(string? voidedAuthorizationStatus)
    {
        if (PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return;
        }

        if (PaymentStatus is OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            throw new InvalidOrderStateException("A fulfilled order cannot be cancelled; refund it instead.");
        }

        if (voidedAuthorizationStatus is not null)
        {
            PayPalAuthorizationStatus = voidedAuthorizationStatus;
        }

        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string payPalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        if (PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new InvalidOrderStateException("Only a fulfilled order can be refunded.");
        }

        var remaining = RemainingRefundable();
        if (amount > remaining)
        {
            throw new InvalidOrderStateException(
                $"Refund of {amount} exceeds the remaining captured amount of {remaining}.");
        }

        var refund = new OrderRefund(payPalRefundId, status, amount, currency, idempotencyKey);
        _refunds.Add(refund);

        var refunded = RefundedTotal();
        var captured = CapturedAmount ?? 0m;
        PaymentStatus = refunded >= captured
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
        PayPalCaptureStatus = PaymentStatus == OrderPaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";

        return refund;
    }
}

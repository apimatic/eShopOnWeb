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
        Payment = new OrderPayment();
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public OrderPayment Payment { get; private set; } = new OrderPayment();

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
        return total;
    }

    public decimal RefundedTotal()
    {
        decimal total = 0m;
        foreach (var refund in _refunds)
        {
            total += refund.Amount;
        }
        return total;
    }

    public decimal RefundableRemaining()
    {
        var captured = Payment.CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    public void EnsureOwnedBy(string buyerId)
    {
        if (!BelongsTo(buyerId))
        {
            throw new PaymentException("Order was not found.", 404, "ORDER_NOT_FOUND");
        }
    }

    public void RecordPayPalOrder(string payPalOrderId, string? status, string currency, string? invoiceId = null)
    {
        EnsurePayment();
        Payment.RecordPayPalOrder(payPalOrderId, status, currency, invoiceId);
    }

    public void AssignInvoiceId(string invoiceId)
    {
        EnsurePayment();
        Payment.AssignInvoiceId(invoiceId);
    }

    public void MarkAuthorized(
        string authorizationId,
        string? authorizationStatus,
        DateTimeOffset? expiration,
        decimal authorizedAmount,
        string currency,
        int? savedPaymentMethodId)
    {
        if (Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return;
        }

        if (Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be authorized.", 409, "INVALID_ORDER_STATE");
        }

        EnsurePayment();
        Payment.RecordAuthorization(authorizationId, authorizationStatus, expiration, authorizedAmount, currency);
        if (savedPaymentMethodId.HasValue)
        {
            Payment.UseSavedPaymentMethod(savedPaymentMethodId.Value);
        }

        Status = OrderStatus.Authorized;
    }

    public void MarkFulfilled(
        string captureId,
        string? captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        string currency)
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return;
        }

        if (Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be fulfilled.", 409, "INVALID_ORDER_STATE");
        }

        if (Status != OrderStatus.Authorized)
        {
            throw new PaymentException("The order must be authorized before it can be fulfilled.", 409, "INVALID_ORDER_STATE");
        }

        EnsurePayment();
        Payment.RecordCapture(captureId, captureStatus, capturedAmount, paypalFee, netAmount, currency);
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled(string? authorizationStatus = null)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentException("A fulfilled order cannot be cancelled; issue a refund instead.", 409, "INVALID_ORDER_STATE");
        }

        EnsurePayment();
        Payment.RecordVoid(authorizationStatus);
        Status = OrderStatus.Cancelled;
    }

    public PaymentRefund AddRefund(string payPalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new PaymentException("Only a fulfilled order can be refunded.", 409, "INVALID_ORDER_STATE");
        }

        var remaining = RefundableRemaining();
        if (amount <= 0)
        {
            throw new PaymentException("Refund amount must be greater than zero.", 400, "INVALID_REFUND_AMOUNT");
        }

        if (amount > remaining)
        {
            throw new PaymentException(
                $"Refund amount {amount} exceeds the remaining refundable amount {remaining}.",
                409,
                "REFUND_EXCEEDS_CAPTURE");
        }

        var refund = new PaymentRefund(payPalRefundId, status, amount, currency, idempotencyKey);
        _refunds.Add(refund);

        Status = RefundableRemaining() == 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    private void EnsurePayment()
    {
        Payment ??= new OrderPayment();
    }
}

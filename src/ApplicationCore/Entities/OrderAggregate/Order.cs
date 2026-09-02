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

    public OrderStatus Status { get; private set; } = OrderStatus.PendingPayment;

    // Payment state owned by PayPal: ids and last-known status for the hold, the capture and the refunds,
    // so a later request can act on the payment, not only the one that started it.
    public string? Currency { get; private set; }
    public string? PaymentInvoiceId { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiryTime { get; private set; }
    public string? CaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }

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

    public void AssignPaymentInvoiceId(string paymentInvoiceId)
    {
        Guard.Against.NullOrEmpty(paymentInvoiceId, nameof(paymentInvoiceId));
        if (Status != OrderStatus.PendingPayment)
        {
            throw new PaymentStateException($"Order {Id} is not awaiting payment (current state: {Status}).");
        }

        PaymentInvoiceId = paymentInvoiceId;
    }

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiryTime, string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != OrderStatus.PendingPayment)
        {
            throw new PaymentStateException($"Order {Id} is not awaiting payment (current state: {Status}).");
        }

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiryTime = expiryTime;
        Currency = currency;
        Status = OrderStatus.PaymentAuthorized;
    }

    public void RecordRenewedAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiryTime)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new PaymentStateException($"Order {Id} has no active authorization to renew (current state: {Status}).");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiryTime = expiryTime;
    }

    public void RecordCapture(string captureId, decimal grossAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new PaymentStateException($"Order {Id} is not in an authorized state (current state: {Status}).");
        }

        CaptureId = captureId;
        CapturedAmount = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status != OrderStatus.PendingPayment && Status != OrderStatus.PaymentAuthorized)
        {
            throw new PaymentStateException($"Order {Id} cannot be cancelled once fulfilled (current state: {Status}); issue a refund instead.");
        }

        if (AuthorizationId != null)
        {
            AuthorizationStatus = "VOIDED";
        }
        Status = OrderStatus.Cancelled;
    }

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - RefundedAmount;

    public OrderRefund RegisterRefund(string payPalRefundId, decimal amount, string refundStatus, string idempotencyKey)
    {
        if (Status != OrderStatus.Fulfilled && Status != OrderStatus.PartiallyRefunded)
        {
            throw new PaymentStateException($"Order {Id} has no captured payment to refund (current state: {Status}).");
        }
        if (amount > RefundableAmount)
        {
            throw new PaymentStateException(
                $"Refund of {amount:0.00} exceeds the remaining refundable amount {RefundableAmount:0.00} on order {Id}.");
        }

        var refund = new OrderRefund(Id, payPalRefundId, amount, refundStatus, idempotencyKey);
        _refunds.Add(refund);
        RefundedAmount += amount;
        Status = RefundedAmount >= (CapturedAmount ?? 0m) ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }
}

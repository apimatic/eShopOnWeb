using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

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
        PaymentAttemptKey = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public string PaymentAttemptKey { get; private set; } = Guid.NewGuid().ToString("N");
    public string? PayPalInvoiceId { get; private set; }

    public string? PayPalCheckoutOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public string? Currency { get; private set; }
    public decimal RefundedAmount { get; private set; }

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

    public decimal RemainingRefundable(string currency)
    {
        var captured = CapturedAmount ?? 0m;
        return PayPalMoneyFormatter.Round(captured - RefundedAmount, currency);
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public void RecordAuthorization(
        string checkoutOrderId,
        string authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        string currency,
        DateTimeOffset? expiresAt,
        DateTimeOffset? createdAt,
        string invoiceId)
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new OrderPaymentException($"Order {Id} cannot be authorized in status {Status}.", 409, "INVALID_ORDER_STATE");
        }

        PayPalCheckoutOrderId = checkoutOrderId;
        PayPalInvoiceId = invoiceId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        AuthorizationExpiresAt = expiresAt;
        AuthorizationCreatedAt = createdAt;
        Status = OrderStatus.Authorized;
    }

    public void RefreshAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt, DateTimeOffset? createdAt)
    {
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        AuthorizationCreatedAt = createdAt;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netProceeds,
        string currency)
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new OrderPaymentException($"Order {Id} cannot be fulfilled in status {Status}.", 409, "INVALID_ORDER_STATE");
        }

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetProceeds = netProceeds;
        Currency = currency;
        PayPalAuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
    }

    public void RecordCancellation(string? authorizationStatus)
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new OrderPaymentException($"Order {Id} cannot be cancelled after fulfilment.", 409, "INVALID_ORDER_STATE");
        }

        if (authorizationStatus != null)
        {
            PayPalAuthorizationStatus = authorizationStatus;
        }

        Status = OrderStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string payPalRefundId, string payPalRefundStatus, decimal amount, string currency, string idempotencyKey)
    {
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new OrderPaymentException($"Order {Id} cannot be refunded in status {Status}.", 409, "INVALID_ORDER_STATE");
        }

        var remaining = RemainingRefundable(currency);
        if (amount > remaining)
        {
            throw new OrderPaymentException(
                $"Refund amount {PayPalMoneyFormatter.Format(amount, currency)} exceeds remaining refundable {PayPalMoneyFormatter.Format(remaining, currency)}.",
                400,
                "REFUND_EXCEEDS_CAPTURE");
        }

        var refund = new OrderRefund(payPalRefundId, payPalRefundStatus, amount, currency, idempotencyKey);
        _refunds.Add(refund);
        RefundedAmount = PayPalMoneyFormatter.Round(RefundedAmount + amount, currency);
        Status = RemainingRefundable(currency) <= 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        if (PayPalCaptureStatus != null)
        {
            PayPalCaptureStatus = Status == OrderStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        }

        return refund;
    }
}

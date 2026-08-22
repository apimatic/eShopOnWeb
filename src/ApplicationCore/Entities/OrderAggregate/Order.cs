using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;

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
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? PayPalAuthorizationExpiration { get; private set; }
    public DateTimeOffset? PayPalAuthorizationCreated { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? PaymentCurrency { get; private set; }
    public string? PaymentInvoiceId { get; private set; }

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

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.Ordinal);

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        foreach (var refund in _refunds)
        {
            if (string.Equals(refund.IdempotencyKey, idempotencyKey, StringComparison.Ordinal))
                return refund;
        }

        return null;
    }

    public decimal TotalRefunded()
    {
        var total = 0m;
        foreach (var refund in _refunds)
        {
            if (refund.CountsAgainstCapturedAmount)
                total += refund.Amount;
        }

        return total;
    }

    public decimal RemainingRefundable()
    {
        if (CapturedAmount is null)
            return 0m;

        var remaining = CapturedAmount.Value - TotalRefunded();
        return remaining < 0 ? 0 : remaining;
    }

    public void RecordAuthorization(
        string paypalOrderId,
        string paypalOrderStatus,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiration,
        DateTimeOffset? created,
        string currency,
        string? invoiceId = null)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        if (Status == OrderStatus.Cancelled)
            throw new PaymentException("This order has been cancelled.", 409);
        if (IsCaptured())
            throw new PaymentException("This order has already been captured.", 409);

        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = paypalOrderStatus;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        PayPalAuthorizationExpiration = expiration;
        PayPalAuthorizationCreated = created;
        PaymentCurrency = currency;
        if (!string.IsNullOrEmpty(invoiceId))
            PaymentInvoiceId = invoiceId;
        Status = OrderStatus.Authorized;
    }

    public void RefreshAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiration, DateTimeOffset? created)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        PayPalAuthorizationExpiration = expiration;
        PayPalAuthorizationCreated = created;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal? paypalFee, decimal? netAmount, string currency)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        if (Status == OrderStatus.Cancelled)
            throw new PaymentException("This order has been cancelled.", 409);

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        PaymentCurrency = currency;
        PayPalAuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
    }

    public void Cancel(string? authorizationStatus = "VOIDED")
    {
        if (Status == OrderStatus.Cancelled)
            return;
        if (IsCaptured())
            throw new PaymentException("A fulfilled order cannot be cancelled. Issue a refund instead.", 409);

        PayPalAuthorizationStatus = authorizationStatus;
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string paypalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(paypalRefundId, nameof(paypalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        if (!IsCaptured())
            throw new PaymentException("Refunds are only allowed after the order has been fulfilled.", 409);

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
            return existing;

        var remaining = RemainingRefundable();
        var roundedAmount = PayPalMoney.Round(amount, currency);
        if (roundedAmount <= 0)
            throw new PaymentException("Refund amount must be greater than zero.");
        if (roundedAmount > remaining)
            throw new PaymentException($"Refund of {PayPalMoney.Format(roundedAmount, currency)} exceeds the remaining refundable amount of {PayPalMoney.Format(remaining, currency)}.");

        var refund = new OrderRefund(Id, paypalRefundId, status, roundedAmount, currency, idempotencyKey);
        _refunds.Add(refund);

        if (RemainingRefundable() == 0)
            Status = OrderStatus.Refunded;
        else
            Status = OrderStatus.PartiallyRefunded;

        PayPalCaptureStatus = Status == OrderStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return refund;
    }

    public bool IsCaptured() =>
        Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded
        || !string.IsNullOrEmpty(PayPalCaptureId);
}

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
        Status = ShopOrderStatus.AwaitingPayment;
        Payment = new OrderPayment();
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public ShopOrderStatus Status { get; private set; } = ShopOrderStatus.AwaitingPayment;
    public OrderPayment Payment { get; private set; } = new();

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    private readonly List<OrderRefundRecord> _refunds = new List<OrderRefundRecord>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx 
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
    public IReadOnlyCollection<OrderRefundRecord> Refunds => _refunds.AsReadOnly();

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

    public OrderRefundRecord? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public decimal RemainingRefundable()
    {
        if (Payment.CapturedAmount is null)
        {
            return 0m;
        }

        var refunded = _refunds
            .Where(r => !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.Amount);

        var remaining = Payment.CapturedAmount.Value - refunded;
        return remaining < 0 ? 0m : remaining;
    }

    public void RecordPayPalOrderCreated(string payPalOrderId, string createRequestId, string currency, string invoiceId)
    {
        Payment.EnsureCreateRequestId(createRequestId);
        Payment.RecordPayPalOrder(payPalOrderId, currency, invoiceId);
    }

    public void MarkAuthorized(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiration,
        DateTimeOffset? createTime,
        string createRequestId,
        string authorizeRequestId,
        string currency)
    {
        Payment.EnsureCreateRequestId(createRequestId);
        Payment.EnsureAuthorizeRequestId(authorizeRequestId);
        Payment.RecordPayPalOrder(payPalOrderId, currency);
        Payment.RecordAuthorization(authorizationId, authorizationStatus, expiration, createTime, currency);
        Status = ShopOrderStatus.Authorized;
    }

    public void MarkReauthorized(string authorizationId, string status, DateTimeOffset? expiration)
    {
        Payment.ReplaceAuthorization(authorizationId, status, expiration);
    }

    public void MarkFulfilled(
        string captureId,
        string captureStatus,
        decimal? capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        string? currency,
        string captureRequestId)
    {
        Payment.EnsureCaptureRequestId(captureRequestId);
        Payment.RecordCapture(captureId, captureStatus, capturedAmount, paypalFee, netAmount, currency);
        Status = ShopOrderStatus.Fulfilled;
    }

    public void MarkCancelled(string? authorizationStatus, string voidRequestId)
    {
        Payment.EnsureVoidRequestId(voidRequestId);
        if (!string.IsNullOrWhiteSpace(authorizationStatus))
        {
            Payment.RecordVoid(authorizationStatus);
        }
        Status = ShopOrderStatus.Cancelled;
    }

    public OrderRefundRecord AddRefund(
        string payPalRefundId,
        string status,
        decimal amount,
        string currency,
        string idempotencyKey,
        decimal? totalRefundedAmount)
    {
        var refund = new OrderRefundRecord(
            payPalRefundId,
            status,
            amount,
            currency,
            idempotencyKey,
            totalRefundedAmount);
        _refunds.Add(refund);

        var remaining = RemainingRefundable();
        Status = remaining <= 0m ? ShopOrderStatus.Refunded : ShopOrderStatus.PartiallyRefunded;
        if (Payment.CaptureStatus is not null && remaining <= 0m)
        {
            Payment.RecordCapture(
                Payment.CaptureId!,
                "REFUNDED",
                Payment.CapturedAmount,
                Payment.PaypalFee,
                Payment.NetAmount,
                Payment.Currency);
        }
        else if (Payment.CaptureStatus is not null)
        {
            Payment.RecordCapture(
                Payment.CaptureId!,
                "PARTIALLY_REFUNDED",
                Payment.CapturedAmount,
                Payment.PaypalFee,
                Payment.NetAmount,
                Payment.Currency);
        }

        return refund;
    }
}

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
    }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items,
        string currency, Guid paymentCorrelationId) : this(buyerId, shipToAddress, items)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Currency = currency.ToUpperInvariant();
        OrderTotal = Total();
        PaymentCorrelationId = paymentCorrelationId;
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public decimal OrderTotal { get; private set; }
    public string? Currency { get; private set; }
    public Guid? PaymentCorrelationId { get; private set; }
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.NotRequired;
    public int PaymentVersion { get; private set; }
    public int? PaymentMethodId { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public string? AuthorizationCreateTime { get; private set; }
    public string? AuthorizationUpdateTime { get; private set; }
    public string? AuthorizationExpirationTime { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalGrossAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? MerchantNetAmount { get; private set; }
    public string? CaptureCreateTime { get; private set; }
    public string? CaptureUpdateTime { get; private set; }

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

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal ReservedOrRefundedAmount => _refunds.Where(x => x.ReservesFunds).Sum(x => x.Amount);
    public decimal RefundableAmount => Math.Max(0m, (CapturedAmount ?? 0m) - ReservedOrRefundedAmount);

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void RecordPayPalOrder(string payPalOrderId, string? status)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = status;
    }

    public void RecordAuthorization(string authorizationId, string status, decimal amount,
        string? createTime, string? updateTime, string? expirationTime, int? paymentMethodId)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationCreateTime = createTime;
        AuthorizationUpdateTime = updateTime;
        AuthorizationExpirationTime = expirationTime;
        PaymentMethodId = paymentMethodId;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void RefreshAuthorization(string authorizationId, string status, decimal amount,
        string? createTime, string? updateTime, string? expirationTime)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationCreateTime = createTime;
        AuthorizationUpdateTime = updateTime;
        AuthorizationExpirationTime = expirationTime;
    }

    public void RecordCapture(string captureId, string status, decimal amount, decimal gross,
        decimal? fee, decimal? net, string? createTime, string? updateTime)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalGrossAmount = gross;
        PayPalFee = fee;
        MerchantNetAmount = net;
        CaptureCreateTime = createTime;
        CaptureUpdateTime = updateTime;
        PaymentStatus = status == "COMPLETED"
            ? OrderPaymentStatus.Fulfilled
            : OrderPaymentStatus.CapturePending;
    }

    public void MarkCancelled(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public PaymentRefund ReserveRefund(string idempotencyKey, decimal amount)
    {
        var existing = _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing != null) return existing;
        if (CapturedAmount == null || amount <= 0m || amount > RefundableAmount)
        {
            throw new InvalidOperationException("The refund amount exceeds the remaining captured amount.");
        }

        var refund = new PaymentRefund(idempotencyKey, amount, Currency!);
        _refunds.Add(refund);
        PaymentVersion++;
        return refund;
    }

    public void RecalculateRefundStatus()
    {
        var refunded = ReservedOrRefundedAmount;
        if (CapturedAmount.HasValue && refunded >= CapturedAmount.Value)
        {
            PaymentStatus = OrderPaymentStatus.Refunded;
            CaptureStatus = "REFUNDED";
        }
        else if (refunded > 0m)
        {
            PaymentStatus = OrderPaymentStatus.PartiallyRefunded;
            CaptureStatus = "PARTIALLY_REFUNDED";
        }
    }
}

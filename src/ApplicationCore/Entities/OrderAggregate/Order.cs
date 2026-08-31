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

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.None;
    public string? PaymentCurrency { get; private set; }
    public decimal PaymentAmount { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? LastReauthorizedAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }
    public bool UsedOneOffCard { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? LastPaymentError { get; private set; }
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

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

    public decimal RefundedTotal() => _refunds
        .Where(r => r.Status is not "FAILED" and not "CANCELLED")
        .Sum(r => r.Amount);

    public void BeginPayment(string currency)
    {
        if (PaymentStatus != PaymentStatus.None)
        {
            throw new InvalidOperationException("Payment has already been initialized for this order.");
        }

        PaymentCurrency = currency;
        PaymentAmount = decimal.Round(Total(), 2, MidpointRounding.AwayFromZero);
        PaymentStatus = PaymentStatus.AwaitingPayment;
        Touch();
    }

    public void RecordAuthorization(string payPalOrderId, string payPalOrderStatus,
        string authorizationId, string authorizationStatus, decimal amount,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt, int? savedPaymentMethodId)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        SavedPaymentMethodId = savedPaymentMethodId;
        UsedOneOffCard = !savedPaymentMethodId.HasValue;
        PaymentStatus = PaymentStatus.Authorized;
        LastPaymentError = null;
        Touch();
    }

    public void RecordReauthorization(string authorizationId, string status, decimal amount,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        LastReauthorizedAt = DateTimeOffset.UtcNow;
        LastPaymentError = null;
        Touch();
    }

    public void MarkAuthorizationRenewalRequired(string message)
    {
        PaymentStatus = PaymentStatus.AuthorizationRenewalRequired;
        LastPaymentError = message;
        Touch();
    }

    public void MarkPayerActionRequired(string message)
    {
        PaymentStatus = PaymentStatus.PayerActionRequired;
        LastPaymentError = message;
        Touch();
    }

    public void RecordCapture(string captureId, string status, decimal grossAmount,
        decimal? payPalFee, decimal? netProceeds)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = grossAmount;
        PayPalFee = payPalFee;
        NetProceeds = netProceeds;
        AuthorizationStatus = "CAPTURED";
        PaymentStatus = PaymentStatus.Fulfilled;
        FulfilledAt = DateTimeOffset.UtcNow;
        LastPaymentError = null;
        Touch();
    }

    public void Cancel(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        PayPalOrderStatus = "VOIDED";
        PaymentStatus = PaymentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        LastPaymentError = null;
        Touch();
    }

    public PaymentRefund AddRefund(string idempotencyKey, string providerRefundId,
        string status, decimal amount)
    {
        var refund = new PaymentRefund(idempotencyKey, providerRefundId, status, amount);
        _refunds.Add(refund);
        ApplyRefundState();
        Touch();
        return refund;
    }

    public void ApplyRefundState()
    {
        var refunded = RefundedTotal();
        if (CapturedAmount.HasValue && refunded >= CapturedAmount.Value)
        {
            PaymentStatus = PaymentStatus.Refunded;
            CaptureStatus = "REFUNDED";
        }
        else if (refunded > 0)
        {
            PaymentStatus = PaymentStatus.PartiallyRefunded;
            CaptureStatus = "PARTIALLY_REFUNDED";
        }
    }

    private void Touch() => ConcurrencyToken = Guid.NewGuid();
}

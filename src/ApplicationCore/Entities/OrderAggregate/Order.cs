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
        : this(buyerId, items)
    {
        ShipToAddress = shipToAddress;
    }

    public Order(string buyerId, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));

        BuyerId = buyerId;
        _orderItems = items;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address? ShipToAddress { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string PaymentReference { get; private set; } = $"eshop-{Guid.NewGuid():N}";
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;
    public FulfilmentStatus FulfilmentStatus { get; private set; } = FulfilmentStatus.Pending;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? PayPalAuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? PayPalAuthorizationExpiresAt { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string CreatePaymentRequestId { get; private set; } = string.Empty;
    public string AuthorizePaymentRequestId { get; private set; } = string.Empty;
    public string CapturePaymentRequestId { get; private set; } = string.Empty;
    public string VoidPaymentRequestId { get; private set; } = string.Empty;
    public int ReauthorizationSequence { get; private set; }
    public string? ReauthorizePaymentRequestId { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

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
        .Where(x => x.Status is "CREATING" or "COMPLETED" or "PENDING")
        .Sum(x => x.Amount);

    public void InitializePayment(string currency)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        if (string.IsNullOrEmpty(Currency)) Currency = currency.ToUpperInvariant();
        if (string.IsNullOrEmpty(PaymentReference)) PaymentReference = $"eshop-{Guid.NewGuid():N}";
    }

    public void EnsurePaymentRequestIds()
    {
        if (Id <= 0) throw new InvalidOperationException("The order must be persisted before payment request identifiers are created.");
        if (string.IsNullOrEmpty(PaymentReference)) PaymentReference = $"eshop-{Guid.NewGuid():N}";
        if (string.IsNullOrEmpty(CreatePaymentRequestId)) CreatePaymentRequestId = $"{PaymentReference}-create";
        if (string.IsNullOrEmpty(AuthorizePaymentRequestId)) AuthorizePaymentRequestId = $"{PaymentReference}-authorize";
        if (string.IsNullOrEmpty(CapturePaymentRequestId)) CapturePaymentRequestId = $"{PaymentReference}-capture";
        if (string.IsNullOrEmpty(VoidPaymentRequestId)) VoidPaymentRequestId = $"{PaymentReference}-void";
    }

    public void RecordPayPalOrder(string id, string? status)
    {
        PayPalOrderId = id;
        PayPalOrderStatus = status;
    }

    public string StartOrResumeReauthorization()
    {
        if (!string.IsNullOrEmpty(ReauthorizePaymentRequestId)) return ReauthorizePaymentRequestId;
        ReauthorizationSequence++;
        ReauthorizePaymentRequestId = $"{PaymentReference}-reauthorize-{ReauthorizationSequence}";
        return ReauthorizePaymentRequestId;
    }

    public void CompleteReauthorization() => ReauthorizePaymentRequestId = null;

    public void RecordAuthorization(string id, string? status, DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        PayPalAuthorizationId = id;
        PayPalAuthorizationStatus = status;
        PayPalAuthorizationCreatedAt = createdAt;
        PayPalAuthorizationExpiresAt = expiresAt;
        PaymentStatus = status switch
        {
            "CREATED" => PaymentStatus.Authorized,
            "PENDING" => PaymentStatus.AuthorizationPending,
            "DENIED" => PaymentStatus.Failed,
            "VOIDED" => PaymentStatus.Cancelled,
            _ => PaymentStatus.AuthorizationPending
        };
    }

    public void MarkPayerActionRequired(string? orderStatus)
    {
        PayPalOrderStatus = orderStatus;
        PaymentStatus = PaymentStatus.PayerActionRequired;
    }

    public void RecordCapture(string id, string? status, decimal amount, decimal? fee, decimal? net, DateTimeOffset? capturedAt)
    {
        PayPalCaptureId = id;
        PayPalCaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetProceeds = net;
        CapturedAt = capturedAt;
        PaymentStatus = status switch
        {
            "COMPLETED" => PaymentStatus.Captured,
            "PENDING" => PaymentStatus.CapturePending,
            "DECLINED" or "FAILED" => PaymentStatus.Failed,
            _ => PaymentStatus.CapturePending
        };

        if (status == "COMPLETED")
        {
            FulfilmentStatus = FulfilmentStatus.Fulfilled;
            FulfilledAt ??= DateTimeOffset.UtcNow;
        }
    }

    public void RecordVoid(string? authorizationStatus)
    {
        PayPalAuthorizationStatus = authorizationStatus;
        PaymentStatus = PaymentStatus.Cancelled;
        FulfilmentStatus = FulfilmentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund AddRefund(string idempotencyKey, decimal amount)
    {
        var refund = new PaymentRefund(idempotencyKey, amount, Currency);
        _refunds.Add(refund);
        return refund;
    }

    public void RefreshRefundState()
    {
        var refunded = RefundedTotal();
        if (CapturedAmount is null || refunded <= 0) return;
        PaymentStatus = refunded >= CapturedAmount.Value
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }
}

public enum PaymentStatus
{
    AwaitingPayment,
    AuthorizationPending,
    Authorized,
    PayerActionRequired,
    CapturePending,
    Captured,
    PartiallyRefunded,
    Refunded,
    Cancelled,
    Failed
}

public enum FulfilmentStatus
{
    Pending,
    Fulfilled,
    Cancelled
}

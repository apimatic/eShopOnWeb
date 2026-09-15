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

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? CurrencyCode { get; private set; }
    public string? LastPaymentError { get; private set; }
    public string? PayAuthorizeRequestId { get; private set; }
    public string? PayCaptureRequestId { get; private set; }
    public string? PayVoidRequestId { get; private set; }

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

    public decimal RefundedTotal() => _refunds
        .Where(r => !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
        .Sum(r => r.Amount);

    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    public void SetCurrency(string currencyCode)
    {
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        CurrencyCode = currencyCode;
    }

    public void MarkPaymentFailed(string message)
    {
        LastPaymentError = message;
        if (PaymentStatus == OrderPaymentStatus.AwaitingPayment)
        {
            PaymentStatus = OrderPaymentStatus.Failed;
        }
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expirationTime,
        string currencyCode)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpirationTime = expirationTime;
        CurrencyCode = currencyCode;
        LastPaymentError = null;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void UpdateAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expirationTime)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpirationTime = expirationTime;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        PayPalAuthorizationStatus = "CAPTURED";
        LastPaymentError = null;
        PaymentStatus = OrderPaymentStatus.Captured;
    }

    public void RecordVoid(string authorizationStatus)
    {
        PayPalAuthorizationStatus = authorizationStatus;
        LastPaymentError = null;
        PaymentStatus = OrderPaymentStatus.Voided;
    }

    public OrderRefund RecordRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var refund = new OrderRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        RefreshRefundStatus();
        return refund;
    }

    public void RefreshRefundStatus()
    {
        if (PaymentStatus is not OrderPaymentStatus.Captured
            and not OrderPaymentStatus.PartiallyRefunded
            and not OrderPaymentStatus.Refunded)
        {
            return;
        }

        var remaining = RemainingRefundable();
        if (remaining <= 0m)
        {
            PaymentStatus = OrderPaymentStatus.Refunded;
            PayPalCaptureStatus = "REFUNDED";
        }
        else if (RefundedTotal() > 0m)
        {
            PaymentStatus = OrderPaymentStatus.PartiallyRefunded;
            PayPalCaptureStatus = "PARTIALLY_REFUNDED";
        }
    }

    public void EnsureOwnedBy(string buyerId)
    {
        if (!BelongsTo(buyerId))
        {
            throw new CheckoutException(404, "Order not found.");
        }
    }

    public void EnsureCanAuthorize()
    {
        if (PaymentStatus is OrderPaymentStatus.Authorized or OrderPaymentStatus.Captured
            or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
        {
            return;
        }

        if (PaymentStatus is OrderPaymentStatus.Voided)
        {
            throw new CheckoutException(409, "This order was cancelled and cannot be paid.");
        }
    }

    public bool AlreadyAuthorized() =>
        PaymentStatus is OrderPaymentStatus.Authorized or OrderPaymentStatus.Captured
            or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded
        && !string.IsNullOrEmpty(PayPalAuthorizationId);

    public void EnsureCanCapture()
    {
        if (PaymentStatus is OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            return;
        }

        if (PaymentStatus == OrderPaymentStatus.Voided)
        {
            throw new CheckoutException(409, "This order was cancelled and cannot be fulfilled.");
        }

        if (PaymentStatus != OrderPaymentStatus.Authorized || string.IsNullOrEmpty(PayPalAuthorizationId))
        {
            throw new CheckoutException(409, "This order has no payment hold to capture. Authorize payment first.");
        }
    }

    public bool AlreadyCaptured() =>
        !string.IsNullOrEmpty(PayPalCaptureId)
        && PaymentStatus is OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded;

    public void EnsureCanVoid()
    {
        if (PaymentStatus == OrderPaymentStatus.Voided)
        {
            return;
        }

        if (PaymentStatus is OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            throw new CheckoutException(409, "This order has already been fulfilled. Cancel is not available; issue a refund instead.");
        }

        if (PaymentStatus != OrderPaymentStatus.Authorized && PaymentStatus != OrderPaymentStatus.AwaitingPayment
            && PaymentStatus != OrderPaymentStatus.Failed)
        {
            throw new CheckoutException(409, "This order cannot be cancelled in its current state.");
        }
    }

    public void EnsureCanRefund(decimal amount)
    {
        if (PaymentStatus is not OrderPaymentStatus.Captured and not OrderPaymentStatus.PartiallyRefunded)
        {
            throw new CheckoutException(409, "Refunds are only available after the order has been fulfilled.");
        }

        if (string.IsNullOrEmpty(PayPalCaptureId))
        {
            throw new CheckoutException(409, "This order has no captured payment to refund.");
        }

        var remaining = RemainingRefundable();
        if (amount <= 0m)
        {
            throw new CheckoutException(400, "Refund amount must be greater than zero.");
        }

        if (amount > remaining)
        {
            throw new CheckoutException(400,
                $"Refund of {amount} exceeds remaining refundable amount {remaining}.");
        }
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public string GetOrCreateAuthorizeRequestId()
    {
        if (string.IsNullOrEmpty(PayAuthorizeRequestId))
        {
            PayAuthorizeRequestId = $"eshop-pay-{Id}-{Guid.NewGuid():N}";
        }

        return PayAuthorizeRequestId;
    }

    public string GetOrCreateCaptureRequestId()
    {
        if (string.IsNullOrEmpty(PayCaptureRequestId))
        {
            PayCaptureRequestId = $"eshop-capture-{Id}-{Guid.NewGuid():N}";
        }

        return PayCaptureRequestId;
    }

    public string GetOrCreateVoidRequestId()
    {
        if (string.IsNullOrEmpty(PayVoidRequestId))
        {
            PayVoidRequestId = $"eshop-void-{Id}-{Guid.NewGuid():N}";
        }

        return PayVoidRequestId;
    }

    public string GetOrCreateReauthorizeRequestId() => $"eshop-reauth-{Id}-{Guid.NewGuid():N}";

    public bool AuthorizationLooksStale(DateTimeOffset utcNow)
    {
        if (AuthorizationExpirationTime.HasValue && AuthorizationExpirationTime.Value <= utcNow.AddHours(1))
        {
            return true;
        }

        return string.Equals(PayPalAuthorizationStatus, "PENDING", StringComparison.OrdinalIgnoreCase);
    }
}

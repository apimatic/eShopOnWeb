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

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string? Currency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? OriginalPayPalAuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? AuthorizeIdempotencyKey { get; private set; }
    public string? CaptureIdempotencyKey { get; private set; }
    public string? CancelIdempotencyKey { get; private set; }

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

    public decimal RefundedTotal()
    {
        return _refunds.Where(r => r.CountsAgainstCapturedFunds).Sum(r => r.Amount);
    }

    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
    }

    public void SetCurrency(string currency)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Currency = currency;
    }

    public void RememberAuthorizeAttempt(string paypalOrderId, string idempotencyKey)
    {
        PayPalOrderId = paypalOrderId;
        AuthorizeIdempotencyKey = idempotencyKey;
    }

    public void MarkAuthorized(AuthorizationHold hold, string idempotencyKey)
    {
        EnsureCanAuthorize();
        PayPalOrderId = hold.PayPalOrderId;
        PayPalAuthorizationId = hold.AuthorizationId;
        OriginalPayPalAuthorizationId ??= hold.AuthorizationId;
        AuthorizationStatus = hold.Status;
        AuthorizationExpiresAt = hold.ExpiresAt;
        AuthorizedAt ??= hold.CreatedAt ?? DateTimeOffset.UtcNow;
        Currency = hold.Currency;
        AuthorizeIdempotencyKey = idempotencyKey;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void ReplaceAuthorization(AuthorizationHold hold)
    {
        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentException("Only an authorized hold can be renewed.", 409);
        }

        PayPalAuthorizationId = hold.AuthorizationId;
        AuthorizationStatus = hold.Status;
        AuthorizationExpiresAt = hold.ExpiresAt;
    }

    public void MarkCaptured(CaptureDetails capture, string idempotencyKey)
    {
        if (PaymentStatus == OrderPaymentStatus.Captured
            || PaymentStatus == OrderPaymentStatus.Refunded
            || PaymentStatus == OrderPaymentStatus.PartiallyRefunded)
        {
            return;
        }

        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentException($"Order cannot be fulfilled from {PaymentStatus}.", 409);
        }

        PayPalCaptureId = capture.CaptureId;
        CaptureStatus = capture.Status;
        CapturedAmount = capture.CapturedAmount;
        PaypalFee = capture.PaypalFee;
        NetAmount = capture.NetAmount;
        Currency = capture.Currency;
        CaptureIdempotencyKey = idempotencyKey;
        AuthorizationStatus = "CAPTURED";
        PaymentStatus = OrderPaymentStatus.Captured;
    }

    public void MarkCancelled(string idempotencyKey)
    {
        if (PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return;
        }

        if (PaymentStatus == OrderPaymentStatus.Captured
            || PaymentStatus == OrderPaymentStatus.Refunded
            || PaymentStatus == OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException("A fulfilled order cannot be cancelled; refund it instead.", 409);
        }

        PaymentStatus = OrderPaymentStatus.Cancelled;
        AuthorizationStatus = "VOIDED";
        CancelIdempotencyKey = idempotencyKey;
    }

    public OrderRefund RecordRefund(RefundDetails refund, string idempotencyKey)
    {
        if (PaymentStatus != OrderPaymentStatus.Captured
            && PaymentStatus != OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException("Only a captured order can be refunded.", 409);
        }

        var recorded = new OrderRefund(refund.RefundId, idempotencyKey, refund.Amount, refund.Status);
        _refunds.Add(recorded);

        var remaining = RemainingRefundable();
        PaymentStatus = remaining <= 0m ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded;
        CaptureStatus = PaymentStatus == OrderPaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return recorded;
    }

    public void MarkPaymentFailed()
    {
        if (PaymentStatus == OrderPaymentStatus.AwaitingPayment
            || PaymentStatus == OrderPaymentStatus.PaymentFailed)
        {
            PaymentStatus = OrderPaymentStatus.PaymentFailed;
            if (string.IsNullOrEmpty(PayPalAuthorizationId))
            {
                AuthorizeIdempotencyKey = null;
            }
        }
    }

    public bool AuthorizationNeedsRenewal(DateTimeOffset utcNow)
    {
        return AuthorizationExpiresAt.HasValue && AuthorizationExpiresAt.Value <= utcNow;
    }

    public bool AuthorizationPastRenewalWindow(DateTimeOffset utcNow)
    {
        if (!AuthorizedAt.HasValue)
        {
            return false;
        }

        return utcNow - AuthorizedAt.Value > TimeSpan.FromDays(30);
    }

    private void EnsureCanAuthorize()
    {
        if (PaymentStatus == OrderPaymentStatus.Authorized
            || PaymentStatus == OrderPaymentStatus.Captured
            || PaymentStatus == OrderPaymentStatus.Refunded
            || PaymentStatus == OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException($"Order is already {PaymentStatus} and cannot be authorized again.", 409);
        }

        if (PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be paid.", 409);
        }
    }
}

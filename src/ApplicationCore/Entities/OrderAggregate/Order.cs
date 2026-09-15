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
    public static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);
    public static readonly TimeSpan AuthorizationValidityPeriod = TimeSpan.FromDays(29);

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

    public string? Currency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? PayPalAuthorizationCreated { get; private set; }
    public DateTimeOffset? PayPalAuthorizationExpires { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
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

    public decimal RefundedTotal() => _refunds.Sum(r => r.Amount);

    public decimal RefundableRemaining()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.OrdinalIgnoreCase);

    public void AssignCurrency(string currency)
    {
        Currency = currency;
    }

    public void RecordAuthorization(AuthorizationResult authorization, string currency)
    {
        EnsureStatus(OrderStatus.AwaitingPayment, "This order is not awaiting payment.");
        Guard.Against.Null(authorization, nameof(authorization));

        PayPalOrderId = authorization.PayPalOrderId;
        PayPalAuthorizationId = authorization.AuthorizationId;
        PayPalAuthorizationStatus = authorization.AuthorizationStatus;
        PayPalAuthorizationCreated = authorization.CreateTime ?? DateTimeOffset.UtcNow;
        PayPalAuthorizationExpires = authorization.ExpirationTime
            ?? PayPalAuthorizationCreated.Value.Add(AuthorizationValidityPeriod);
        Currency = currency;
        Status = OrderStatus.Authorized;
    }

    public void RecordReauthorization(AuthorizationResult authorization)
    {
        Guard.Against.Null(authorization, nameof(authorization));
        PayPalAuthorizationId = authorization.AuthorizationId;
        PayPalAuthorizationStatus = authorization.AuthorizationStatus;
        PayPalAuthorizationCreated = authorization.CreateTime ?? DateTimeOffset.UtcNow;
        PayPalAuthorizationExpires = authorization.ExpirationTime
            ?? PayPalAuthorizationCreated.Value.Add(AuthorizationValidityPeriod);
    }

    public void RecordCapture(CaptureResult capture)
    {
        EnsureStatus(OrderStatus.Authorized, "Only an authorized order can be fulfilled.");
        Guard.Against.Null(capture, nameof(capture));

        PayPalCaptureId = capture.CaptureId;
        PayPalCaptureStatus = capture.CaptureStatus;
        CapturedAmount = capture.CapturedAmount;
        PaypalFee = capture.PaypalFee;
        NetAmount = capture.NetAmount;
        PayPalAuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
    }

    public void RecordCancellation(bool authorizationWasVoided)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.Authorized)
        {
            throw new PaymentException(409, "Only an unfulfilled order can be cancelled.", "ORDER_NOT_CANCELLABLE");
        }

        if (authorizationWasVoided)
        {
            PayPalAuthorizationStatus = "VOIDED";
        }

        Status = OrderStatus.Cancelled;
    }

    public PaymentRefund RecordRefund(RefundGatewayResult refund, string idempotencyKey)
    {
        if (Status != OrderStatus.Fulfilled && Status != OrderStatus.PartiallyRefunded)
        {
            throw new PaymentException(409, "Refunds can only be issued after the order has been fulfilled.", "ORDER_NOT_REFUNDABLE");
        }

        Guard.Against.Null(refund, nameof(refund));

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var remaining = RefundableRemaining();
        if (refund.Amount - remaining > 0.001m)
        {
            throw new PaymentException(409,
                $"Refund of {refund.Amount} exceeds the remaining captured amount of {remaining}.",
                "REFUND_EXCEEDS_CAPTURE");
        }

        var paymentRefund = new PaymentRefund(refund.RefundId, refund.Status, refund.Amount, refund.Currency, idempotencyKey);
        _refunds.Add(paymentRefund);

        Status = RefundableRemaining() <= 0.001m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return paymentRefund;
    }

    public bool HonorPeriodHasElapsed(DateTimeOffset utcNow)
    {
        if (PayPalAuthorizationCreated is null)
        {
            return false;
        }

        return utcNow >= PayPalAuthorizationCreated.Value.Add(AuthorizationHonorPeriod);
    }

    public bool AuthorizationIsPastRenewalWindow(DateTimeOffset utcNow)
    {
        var expires = PayPalAuthorizationExpires
            ?? PayPalAuthorizationCreated?.Add(AuthorizationValidityPeriod);

        if (expires is null)
        {
            return false;
        }

        return utcNow >= expires.Value;
    }

    public string UnrenewableAuthorizationMessage() =>
        "This authorization can no longer be renewed. PayPal's 29-day authorization window has closed, so the hold cannot be recaptured. Ask the shopper to place and pay a new order, or cancel this one.";

    private void EnsureStatus(OrderStatus expected, string message)
    {
        if (Status != expected)
        {
            throw new PaymentException(409, message, "INVALID_ORDER_STATE");
        }
    }
}

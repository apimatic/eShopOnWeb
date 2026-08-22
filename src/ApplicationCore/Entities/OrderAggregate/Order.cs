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
    #pragma warning restore CS8618

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Status = OrderPaymentStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderPaymentStatus Status { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    public string? PayPalOrderId { get; private set; }
    public string? PayPalInvoiceId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? AuthorizationExpirationTime { get; private set; }
    public string? AuthorizationCreateTime { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? CurrencyCode { get; private set; }
    public string? CreateOrderRequestId { get; private set; }
    public string? AuthorizeRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }
    public string? VoidRequestId { get; private set; }
    public string? ReauthorizeRequestId { get; private set; }

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

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

    public decimal RefundedTotal() => _refunds.Sum(r => r.Amount);

    public decimal RefundableRemaining()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0m : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string key) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == key);

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.Ordinal);

    public void EnsurePayIdempotencyKeys()
    {
        CreateOrderRequestId ??= Guid.NewGuid().ToString("N");
        AuthorizeRequestId ??= Guid.NewGuid().ToString("N");
    }

    public void EnsureCaptureRequestId()
    {
        CaptureRequestId ??= Guid.NewGuid().ToString("N");
    }

    public void EnsureVoidRequestId()
    {
        VoidRequestId ??= Guid.NewGuid().ToString("N");
    }

    public void EnsureReauthorizeRequestId()
    {
        ReauthorizeRequestId ??= Guid.NewGuid().ToString("N");
    }

    public void RecordPayPalOrder(string payPalOrderId, string currencyCode, string invoiceId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        PayPalOrderId = payPalOrderId;
        CurrencyCode = currencyCode;
        PayPalInvoiceId = invoiceId;
    }

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string status, string? expirationTime, string? createTime, string currencyCode)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        if (Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Cancelled
            or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException($"Order {Id} cannot be authorized in status {Status}.", 409);
        }

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpirationTime = expirationTime;
        AuthorizationCreateTime = createTime;
        CurrencyCode = currencyCode;
        Status = OrderPaymentStatus.Authorized;
    }

    public void ReplaceAuthorization(string authorizationId, string status, string? expirationTime, string? createTime)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpirationTime = expirationTime;
        AuthorizationCreateTime = createTime;
    }

    public void MarkFulfilled(string captureId, string captureStatus, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));
        Guard.Against.Negative(capturedAmount, nameof(capturedAmount));

        if (Status != OrderPaymentStatus.Authorized && Status != OrderPaymentStatus.Fulfilled)
        {
            throw new PaymentException($"Order {Id} cannot be fulfilled in status {Status}.", 409);
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = OrderPaymentStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status != OrderPaymentStatus.Authorized && Status != OrderPaymentStatus.AwaitingPayment
            && Status != OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException($"Order {Id} cannot be cancelled in status {Status}.", 409);
        }

        AuthorizationStatus = Status == OrderPaymentStatus.AwaitingPayment ? AuthorizationStatus : "VOIDED";
        Status = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string payPalRefundId, string idempotencyKey, decimal amount, string currency, string status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        if (Status is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException($"Order {Id} cannot be refunded in status {Status}.", 409);
        }

        var remaining = RefundableRemaining();
        if (amount > remaining)
        {
            throw new PaymentException(
                $"Refund of {amount:0.00} exceeds remaining refundable amount {remaining:0.00}.", 400);
        }

        var refund = new OrderRefund(payPalRefundId, idempotencyKey, amount, currency, status);
        _refunds.Add(refund);

        var newRemaining = RefundableRemaining();
        Status = newRemaining <= 0m ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded;
        CaptureStatus = Status == OrderPaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return refund;
    }
}

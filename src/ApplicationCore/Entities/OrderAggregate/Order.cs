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
    private Order()
    {
        Payment = OrderPayment.CreatePending("USD");
    }
    #pragma warning restore CS8618

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Status = OrderStatus.AwaitingPayment;
        Payment = OrderPayment.CreatePending("USD");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; }
    public OrderPayment Payment { get; private set; }

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

    public void SetPaymentCurrency(string currency)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Payment ??= OrderPayment.CreatePending(currency);
        if (string.IsNullOrEmpty(Payment.PayPalOrderId))
        {
            Payment = OrderPayment.CreatePending(currency);
        }
    }

    public string InvoiceId() =>
        !string.IsNullOrEmpty(Payment?.PayPalInvoiceId) ? Payment.PayPalInvoiceId : $"ESHOP-{Id}";

    public string EnsurePayPalInvoiceId()
    {
        EnsurePayment();
        if (string.IsNullOrEmpty(Payment.PayPalInvoiceId))
        {
            Payment.SetPayPalInvoiceId($"E{Id}{Guid.NewGuid():N}");
        }

        return Payment.PayPalInvoiceId!;
    }

    public void ClearPayPalInvoiceId()
    {
        EnsurePayment();
        Payment.SetPayPalInvoiceId(null);
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt)
    {
        EnsurePayment();

        if (Status == OrderStatus.Authorized &&
            string.Equals(Payment.AuthorizationId, authorizationId, StringComparison.Ordinal))
        {
            return;
        }

        if (Status != OrderStatus.AwaitingPayment)
        {
            throw OrderPaymentException.Conflict($"Order {Id} cannot be authorized from status {Status}.");
        }

        Payment.RecordAuthorization(payPalOrderId, authorizationId, authorizationStatus, createdAt, expiresAt);
        Status = OrderStatus.Authorized;
    }

    public string EnsureAuthorizeRequestId()
    {
        EnsurePayment();
        if (string.IsNullOrEmpty(Payment.AuthorizeRequestId))
        {
            Payment.SetAuthorizeRequestId($"eshop-auth-{Id}-{Guid.NewGuid():N}");
        }

        return Payment.AuthorizeRequestId!;
    }

    public string EnsureCaptureRequestId()
    {
        EnsurePayment();
        if (string.IsNullOrEmpty(Payment.CaptureRequestId))
        {
            Payment.SetCaptureRequestId($"eshop-cap-{Id}-{Guid.NewGuid():N}");
        }

        return Payment.CaptureRequestId!;
    }

    public void ClearAuthorizeRequestId()
    {
        EnsurePayment();
        Payment.SetAuthorizeRequestId(null);
    }

    public void ClearCaptureRequestId()
    {
        EnsurePayment();
        Payment.SetCaptureRequestId(null);
    }

    public void RecordReauthorization(
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt)
    {
        EnsurePayment();
        if (Status != OrderStatus.Authorized)
        {
            throw OrderPaymentException.Conflict($"Order {Id} cannot be reauthorized from status {Status}.");
        }

        Payment.RecordReauthorization(authorizationId, authorizationStatus, createdAt, expiresAt);
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal paypalFee,
        decimal netProceeds)
    {
        EnsurePayment();

        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded &&
            string.Equals(Payment.CaptureId, captureId, StringComparison.Ordinal))
        {
            return;
        }

        if (Status != OrderStatus.Authorized)
        {
            throw OrderPaymentException.Conflict($"Order {Id} cannot be fulfilled from status {Status}.");
        }

        Payment.RecordCapture(captureId, captureStatus, capturedAmount, paypalFee, netProceeds);
        Status = OrderStatus.Fulfilled;
    }

    public void Cancel(string authorizationStatus = "VOIDED")
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw OrderPaymentException.Conflict(
                $"Order {Id} has already been fulfilled and cannot be cancelled. Issue a refund instead.");
        }

        if (Status is not (OrderStatus.AwaitingPayment or OrderStatus.Authorized))
        {
            throw OrderPaymentException.Conflict($"Order {Id} cannot be cancelled from status {Status}.");
        }

        EnsurePayment();
        if (!string.IsNullOrEmpty(Payment.AuthorizationId))
        {
            Payment.RecordVoid(authorizationStatus);
        }

        Status = OrderStatus.Cancelled;
    }

    public PaymentRefund RecordRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status)
    {
        EnsurePayment();

        var existing = Payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
        {
            throw OrderPaymentException.Conflict(
                $"Order {Id} cannot be refunded from status {Status}. Fulfil the order before refunding.");
        }

        var remainingCents = ToCents(Payment.RemainingRefundable);
        var refundCents = ToCents(amount);
        if (refundCents > remainingCents)
        {
            throw OrderPaymentException.BadRequest(
                $"Refund of {amount} exceeds the remaining refundable amount {Payment.RemainingRefundable} for order {Id}.");
        }

        var refund = Payment.RecordRefund(payPalRefundId, idempotencyKey, amount, status);
        Status = ToCents(Payment.RemainingRefundable) == 0
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;
        return refund;
    }

    public IEnumerable<string> PayPalIdentifiers()
    {
        EnsurePayment();
        if (!string.IsNullOrEmpty(Payment.PayPalOrderId)) yield return Payment.PayPalOrderId;
        if (!string.IsNullOrEmpty(Payment.AuthorizationId)) yield return Payment.AuthorizationId;
        if (!string.IsNullOrEmpty(Payment.CaptureId)) yield return Payment.CaptureId;
        foreach (var refund in Payment.Refunds)
        {
            yield return refund.PayPalRefundId;
        }
        if (!string.IsNullOrEmpty(Payment.PayPalInvoiceId)) yield return Payment.PayPalInvoiceId;
    }

    private void EnsurePayment()
    {
        Payment ??= OrderPayment.CreatePending("USD");
    }

    private static long ToCents(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}

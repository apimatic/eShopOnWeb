using System;
using System.Collections.Generic;
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

    /// <summary>
    /// A stable, globally-unique seed for this order, generated once at creation. It anchors the
    /// idempotency keys sent to PayPal (authorize/capture/reauthorize) so a double-submit is
    /// deduplicated while remaining unique per order — even if the store's integer ids are reset.
    /// </summary>
    public Guid IdempotencySeed { get; private set; } = Guid.NewGuid();

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

    // ---- Payment state (additive: PayPal integration) ------------------------------------

    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;

    /// <summary>The PayPal-owned payment state for this order; null until the order is paid.</summary>
    public OrderPayment? Payment { get; private set; }

    public bool IsAwaitingPayment => PaymentStatus == PaymentStatus.AwaitingPayment;

    /// <summary>
    /// Records a successful authorization (a hold on the funds). The authorized amount
    /// must equal the order total to the cent.
    /// </summary>
    public void AuthorizePayment(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string currencyCode, int? paymentMethodId)
    {
        Payment = new OrderPayment(payPalOrderId, currencyCode, Total(), paymentMethodId);
        Payment.SetAuthorization(authorizationId, authorizationStatus, expiresAt);
        PaymentStatus = PaymentStatus.Authorized;
    }

    /// <summary>Replaces a stale authorization with a renewed one (after a re-authorization).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.Null(Payment, nameof(Payment));
        Payment!.SetAuthorization(authorizationId, authorizationStatus, expiresAt);
    }

    /// <summary>Records the capture at fulfilment: the money actually taken, plus fee and net proceeds.</summary>
    public void CapturePayment(string captureId, string captureStatus, decimal grossAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.Null(Payment, nameof(Payment));
        Payment!.RecordCapture(captureId, captureStatus, grossAmount, payPalFee, netAmount);
        PaymentStatus = PaymentStatus.Captured;
    }

    /// <summary>Marks the order cancelled before fulfilment (any hold has been released).</summary>
    public void CancelPayment()
    {
        PaymentStatus = PaymentStatus.Cancelled;
    }

    /// <summary>Records a refund against the captured payment and advances the refund state.</summary>
    public void RecordRefund(OrderRefund refund)
    {
        Guard.Against.Null(Payment, nameof(Payment));
        Payment!.AddRefund(refund);
        PaymentStatus = Payment.RefundableRemaining() <= 0m
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }
}

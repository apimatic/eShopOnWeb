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

    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>
    /// Provider-side payment state for this order (PayPal order, authorization, capture).
    /// Null until the order is paid; retained afterwards so later requests (fulfil, cancel,
    /// refund, reconciliation) can act on the money PayPal already holds.
    /// </summary>
    public PaymentDetails Payment { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

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

    public void RecordAuthorization(PaymentDetails payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        Guard.Against.NullOrEmpty(payment.ProviderOrderId, nameof(payment.ProviderOrderId));
        Guard.Against.NullOrEmpty(payment.AuthorizationId, nameof(payment.AuthorizationId));

        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    /// <summary>
    /// The provider order exists but the authorization never confirmed (interrupted attempt).
    /// The order stays AwaitingPayment; the next pay call recovers the authorization instead
    /// of creating a second hold.
    /// </summary>
    public void RecordPendingProviderOrder(PaymentDetails payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        Guard.Against.NullOrEmpty(payment.ProviderOrderId, nameof(payment.ProviderOrderId));

        Payment = payment;
    }

    /// <summary>Adopts a renewed (re-authorized) authorization, keeping the provider order id.</summary>
    public void RecordRenewedAuthorization(string newAuthorizationId, string newAuthorizationStatus, decimal newAmount, DateTimeOffset? newExpirationTime)
    {
        Guard.Against.Null(Payment, nameof(Payment));
        Payment.RenewAuthorization(newAuthorizationId, newAuthorizationStatus, newAmount, newExpirationTime);
    }

    public void RecordCapture(string captureId, string captureStatus, decimal grossAmount, decimal? feeAmount, decimal? netAmount, DateTimeOffset capturedAt)
    {
        Guard.Against.Null(Payment, nameof(Payment));
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        Payment.RecordCapture(captureId, captureStatus, grossAmount, feeAmount, netAmount, capturedAt);
        Status = OrderStatus.Fulfilled;
    }

    public void Cancel()
    {
        Status = OrderStatus.Cancelled;
        Payment?.MarkVoided();
    }

    public void MarkReleasedWithoutProviderAction()
    {
        Status = OrderStatus.Cancelled;
    }

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        Payment?.RecordRefundSummary(refund.Status);
    }

    /// <summary>Sum of refund amounts that actually consume the capture (completed or pending).</summary>
    public decimal RefundedAmount()
    {
        var refunded = 0m;
        foreach (var refund in _refunds)
        {
            if (refund.ConsumesCaptureAmount)
            {
                refunded += refund.Amount;
            }
        }
        return refunded;
    }

    public decimal RemainingRefundableAmount()
    {
        if (Payment?.CapturedAmount is null)
        {
            return 0m;
        }
        var remaining = Payment.CapturedAmount.Value - RefundedAmount();
        return remaining > 0m ? remaining : 0m;
    }
}

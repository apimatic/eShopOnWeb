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
        Status = OrderStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    /// <summary>Fulfilment lifecycle. Additive to the original model, which had no such state.</summary>
    public OrderStatus Status { get; private set; }

    /// <summary>The money-movement state (hold, capture, refunds) once payment begins. Null until paid.</summary>
    public OrderPayment? Payment { get; private set; }

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

    // ----------------------------------------------------------------------------------------
    // Payment / fulfilment behaviour. Transitions are guarded so the aggregate can never end up
    // in a contradictory state (e.g. fulfilled without an authorization, or refunded past capture).
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// Records that the order total has been authorized with PayPal (a hold placed). Idempotent
    /// at the aggregate level: an order that is already authorized keeps its existing payment.
    /// </summary>
    public OrderPayment AuthorizePayment(string payPalOrderId, string currencyCode, string authorizationId,
        string? authorizationStatus, DateTimeOffset? authorizationExpiresAt, string? instrumentSummary, string? vaultId,
        string? payPalCustomId = null)
    {
        if (Status is OrderStatus.Cancelled)
            throw new InvalidOrderOperationException("A cancelled order cannot be paid.");
        if (Status is OrderStatus.Fulfilled)
            throw new InvalidOrderOperationException("A fulfilled order has already been paid.");

        Payment ??= new OrderPayment(payPalOrderId, Total(), currencyCode, payPalCustomId);
        Payment.SetAuthorized(authorizationId, authorizationStatus, authorizationExpiresAt, instrumentSummary, vaultId);
        Status = OrderStatus.PaymentAuthorized;
        return Payment;
    }

    /// <summary>Replaces a stale hold with a renewed one, keeping the order authorized.</summary>
    public void RenewAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        GuardHasPayment();
        if (Status != OrderStatus.PaymentAuthorized)
            throw new InvalidOrderOperationException("Only an authorized order can have its authorization renewed.");
        Payment!.RenewAuthorization(authorizationId, authorizationStatus, expiresAt);
    }

    /// <summary>Records that the authorized funds have been captured — the order is fulfilled.</summary>
    public void Fulfil(string captureId, string? captureStatus, decimal capturedAmount, decimal? fee, decimal? net)
    {
        GuardHasPayment();
        if (Status == OrderStatus.Fulfilled) return; // idempotent
        if (Status != OrderStatus.PaymentAuthorized)
            throw new InvalidOrderOperationException("Only an authorized order can be fulfilled.");
        Payment!.SetCaptured(captureId, captureStatus, capturedAmount, fee, net);
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Cancels an order before fulfilment, releasing any held funds.</summary>
    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled) return; // idempotent
        if (Status == OrderStatus.Fulfilled)
            throw new InvalidOrderOperationException("A fulfilled order cannot be cancelled; issue a refund instead.");
        Payment?.SetVoided();
        Status = OrderStatus.Cancelled;
    }

    /// <summary>Records a refund against the captured payment (full or partial).</summary>
    public OrderRefund Refund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        GuardHasPayment();
        if (Status != OrderStatus.Fulfilled)
            throw new InvalidOrderOperationException("Only a fulfilled order can be refunded.");
        if (amount > Payment!.RefundableRemaining)
            throw new InvalidOrderOperationException(
                $"Refund of {amount} exceeds the refundable remaining amount of {Payment.RefundableRemaining}.");
        return Payment.AddRefund(payPalRefundId, amount, status, idempotencyKey);
    }

    private void GuardHasPayment()
    {
        if (Payment is null)
            throw new InvalidOrderOperationException("The order has no payment; it must be paid first.");
    }
}

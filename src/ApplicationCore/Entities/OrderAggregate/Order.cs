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
        Status = OrderStatus.AwaitingPayment;
        Payment = new OrderPayment();
        Payment.AssignInvoiceId($"ESHOP-{Guid.NewGuid():N}");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public OrderPayment Payment { get; private set; } = new OrderPayment();

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper)
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
        return _refunds
            .Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.Amount);
    }

    public decimal RemainingRefundable()
    {
        var captured = Payment.CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string payPalOrderStatus,
        string authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        string currency,
        DateTimeOffset? expiresAt,
        string? vaultId,
        int? savedPaymentMethodId)
    {
        EnsureOwnedByCurrentFlow(OrderStatus.AwaitingPayment, OrderStatus.Authorized);
        if (Status == OrderStatus.Authorized && !string.IsNullOrEmpty(Payment.AuthorizationId))
        {
            return;
        }

        Payment.RecordAuthorization(
            payPalOrderId,
            payPalOrderStatus,
            authorizationId,
            authorizationStatus,
            authorizedAmount,
            currency,
            expiresAt,
            vaultId,
            savedPaymentMethodId);
        Status = OrderStatus.Authorized;
    }

    public void RecordRenewedAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Order {Id} must be authorized before the hold can be renewed.");
        }

        Payment.UpdateAuthorization(authorizationId, authorizationStatus, expiresAt);
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netProceeds)
    {
        if (Status == OrderStatus.Fulfilled && !string.IsNullOrEmpty(Payment.CaptureId))
        {
            return;
        }

        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Order {Id} must be authorized before it can be fulfilled.");
        }

        Payment.RecordCapture(captureId, captureStatus, capturedAmount, paypalFee, netProceeds);
        Status = OrderStatus.Fulfilled;
    }

    public void Cancel(string? authorizationStatus = null)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new InvalidOrderStateException($"Order {Id} has already been fulfilled and cannot be cancelled. Issue a refund instead.");
        }

        if (Status is not OrderStatus.AwaitingPayment and not OrderStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Order {Id} cannot be cancelled from status {Status}.");
        }

        Payment.RecordVoid(authorizationStatus);
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string payPalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        if (Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOrderStateException($"Order {Id} must be fulfilled before it can be refunded.");
        }

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var remaining = RemainingRefundable();
        if (amount > remaining)
        {
            throw new InvalidOrderStateException(
                $"Refund of {amount:0.00} exceeds the remaining refundable amount of {remaining:0.00} on order {Id}.");
        }

        var refund = new OrderRefund(payPalRefundId, status, amount, currency, idempotencyKey);
        _refunds.Add(refund);

        var newRemaining = RemainingRefundable();
        Status = newRemaining <= 0 ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }

    private void EnsureOwnedByCurrentFlow(params OrderStatus[] allowed)
    {
        if (allowed.Contains(Status))
        {
            return;
        }

        throw new InvalidOrderStateException($"Order {Id} is {Status} and cannot accept this payment action.");
    }
}

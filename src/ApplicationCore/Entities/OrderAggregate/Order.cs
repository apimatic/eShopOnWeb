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
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    /// <summary>
    /// A stable, unique reference for this order's payment, assigned once at creation. Used to make
    /// the PayPal invoice id and idempotency keys unique per order (so they never collide across
    /// runs) while staying stable across retries of the same order's payment.
    /// </summary>
    public Guid PaymentReference { get; private set; } = Guid.NewGuid();

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

    // ---- Payment / fulfilment state (additive) --------------------------------------------

    /// <summary>The payment/fulfilment lifecycle status. Starts awaiting payment.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>PayPal-owned payment state; null until the order is authorized (paid).</summary>
    public OrderPayment? Payment { get; private set; }

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>
    /// Optimistic-concurrency token. Bumped on every payment state transition so that two
    /// concurrent writes racing on the same transition cannot both succeed (SQL Server enforces
    /// via the WHERE clause; the in-memory provider used for local dev does not — see the plan).
    /// </summary>
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    private void Bump() => ConcurrencyToken = Guid.NewGuid();

    /// <summary>Total already refunded across all recorded refunds.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>
    /// Records that funds were authorized (held) with PayPal. Valid only while awaiting payment.
    /// </summary>
    public void RecordAuthorization(string payPalOrderId, string authorizationId,
        string authorizationStatus, DateTimeOffset? authorizationExpiresAt, string currency,
        int? savedPaymentMethodId)
    {
        if (Status != OrderStatus.AwaitingPayment)
            throw new InvalidOrderPaymentStateException(
                $"Order {Id} cannot be authorized from status {Status}.");

        Payment = new OrderPayment(currency, Total(), payPalOrderId, savedPaymentMethodId);
        Payment.RecordAuthorization(authorizationId, authorizationStatus, authorizationExpiresAt);
        Status = OrderStatus.Authorized;
        Bump();
    }

    /// <summary>
    /// Updates the held authorization after it was renewed (re-authorized) because it went stale
    /// before fulfilment. Does not change order status (still Authorized).
    /// </summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt)
    {
        if (Status != OrderStatus.Authorized || Payment is null)
            throw new InvalidOrderPaymentStateException(
                $"Order {Id} has no active authorization to renew (status {Status}).");

        Payment.RenewAuthorization(authorizationId, authorizationStatus, authorizationExpiresAt);
        Bump();
    }

    /// <summary>
    /// Records that the held funds were captured (taken) at fulfilment, with the amounts PayPal
    /// reported. Valid only from an authorized order.
    /// </summary>
    public void RecordCapture(string captureId, string captureStatus, decimal capturedGross,
        decimal? paypalFee, decimal? netAmount)
    {
        if (Status != OrderStatus.Authorized || Payment is null)
            throw new InvalidOrderPaymentStateException(
                $"Order {Id} cannot be fulfilled from status {Status}.");

        Payment.RecordCapture(captureId, captureStatus, capturedGross, paypalFee, netAmount);
        Status = OrderStatus.Fulfilled;
        Bump();
    }

    /// <summary>
    /// Records that the held funds were released (voided) on cancellation before fulfilment.
    /// </summary>
    public void RecordCancellation()
    {
        if (Status != OrderStatus.Authorized || Payment is null)
            throw new InvalidOrderPaymentStateException(
                $"Order {Id} cannot be cancelled from status {Status}. " +
                "Only an authorized, not-yet-fulfilled order can be cancelled.");

        Payment.SetAuthorizationStatus("VOIDED");
        Status = OrderStatus.Cancelled;
        Bump();
    }

    /// <summary>
    /// The most that could still be refunded without exceeding the captured amount.
    /// </summary>
    public decimal RefundableRemaining()
    {
        if (Payment?.CapturedGross is not decimal captured) return 0m;
        return captured - TotalRefunded();
    }

    /// <summary>
    /// Guards a refund request before any PayPal call: the order must be fulfilled (or already
    /// partially refunded) and the requested amount must not push the cumulative refund beyond
    /// what was captured.
    /// </summary>
    public void GuardRefund(decimal amount)
    {
        if (Status != OrderStatus.Fulfilled && Status != OrderStatus.PartiallyRefunded)
            throw new InvalidOrderPaymentStateException(
                $"Order {Id} cannot be refunded from status {Status}. Only a fulfilled order can be refunded.");
        if (amount <= 0)
            throw new InvalidOrderPaymentStateException("Refund amount must be positive.");
        if (amount > RefundableRemaining())
            throw new InvalidOrderPaymentStateException(
                $"Refund of {amount} exceeds the refundable remaining {RefundableRemaining()} for order {Id}.");
    }

    /// <summary>
    /// Records a completed (or pending) refund against the captured payment and advances the
    /// order to PartiallyRefunded / Refunded.
    /// </summary>
    public OrderRefund RecordRefund(string idempotencyKey, string payPalRefundId, decimal amount,
        string status)
    {
        GuardRefund(amount);

        var refund = new OrderRefund(idempotencyKey, payPalRefundId, amount, status);
        _refunds.Add(refund);

        var remaining = RefundableRemaining();
        Status = remaining <= 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        Payment!.SetCaptureStatus(remaining <= 0m ? "REFUNDED" : "PARTIALLY_REFUNDED");
        Bump();
        return refund;
    }

    /// <summary>Finds an already-recorded refund by its caller idempotency key, if any.</summary>
    public OrderRefund? FindRefundByKey(string idempotencyKey)
        => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

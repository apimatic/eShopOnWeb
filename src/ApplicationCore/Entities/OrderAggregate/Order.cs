using System;
using System.Collections.Generic;
using System.Linq;
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

    private readonly List<PaymentAttempt> _paymentAttempts = new List<PaymentAttempt>();
    public IReadOnlyCollection<PaymentAttempt> PaymentAttempts => _paymentAttempts.AsReadOnly();

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

    /// <summary>The attempt that took the money, if any.</summary>
    public PaymentAttempt? AuthorisedPayment => _paymentAttempts.FirstOrDefault(a => a.Status == PaymentAttemptStatus.Authorised);

    /// <summary>An attempt whose outcome the provider has not confirmed yet, if any.</summary>
    public PaymentAttempt? UnsettledPayment => _paymentAttempts.FirstOrDefault(a => a.IsUnsettled);

    public long PaidAmountMinor => AuthorisedPayment?.AuthorisedAmountMinor ?? 0;

    public long RefundedAmountMinor => _refunds.Where(r => r.Status == RefundStatus.Received).Sum(r => r.AmountMinor);

    /// <summary>
    /// What may still be refunded. Refunds whose outcome is unknown are counted as if they went through,
    /// so the order can never be refunded beyond what was paid.
    /// </summary>
    public long RefundableAmountMinor => PaidAmountMinor - _refunds.Where(r => r.CountsAgainstPayment).Sum(r => r.AmountMinor);

    public bool CanStartPayment => PaymentStatus == OrderPaymentStatus.AwaitingPayment && UnsettledPayment is null;

    public PaymentAttempt StartPaymentAttempt(string idempotencyKey, long amountMinor, string currency, DateTimeOffset now)
    {
        if (!CanStartPayment)
        {
            throw new InvalidOperationException($"Order {Id} cannot start a new payment while in state {PaymentStatus}.");
        }

        var attempt = new PaymentAttempt($"ESHOP-{Id}-PAY-{_paymentAttempts.Count + 1}", idempotencyKey, amountMinor, currency, now);
        _paymentAttempts.Add(attempt);
        RecalculatePaymentStatus();
        return attempt;
    }

    public void RecordPaymentOutcome(PaymentAttempt attempt, PaymentAttemptStatus status, string? pspReference, string? resultCode,
        string? refusalReason, string? refusalReasonCode, string? errorCode, string? errorMessage,
        long? authorisedAmountMinor, DateTimeOffset now)
    {
        EnsureOwns(attempt);
        attempt.RecordOutcome(status, pspReference, resultCode, refusalReason, refusalReasonCode, errorCode, errorMessage, authorisedAmountMinor, now);
        RecalculatePaymentStatus();
    }

    public void AddProviderResponse(PaymentAttempt attempt, ProviderResponseRecord response)
    {
        EnsureOwns(attempt);
        attempt.AddProviderResponse(response);
    }

    public OrderRefund StartRefund(string idempotencyKey, long amountMinor, string requestedBy, DateTimeOffset now)
    {
        var payment = AuthorisedPayment
            ?? throw new InvalidOperationException($"Order {Id} has no authorised payment to refund.");
        Guard.Against.NegativeOrZero(amountMinor, nameof(amountMinor));
        if (amountMinor > RefundableAmountMinor)
        {
            throw new InvalidOperationException($"Order {Id} cannot be refunded {amountMinor}; only {RefundableAmountMinor} is refundable.");
        }

        var refund = new OrderRefund($"ESHOP-{Id}-REFUND-{_refunds.Count + 1}", idempotencyKey, payment.PspReference!,
            amountMinor, payment.Currency, requestedBy, now);
        _refunds.Add(refund);
        return refund;
    }

    public void RecordRefundOutcome(OrderRefund refund, RefundStatus status, string? pspReference, string? errorCode, string? errorMessage, DateTimeOffset now)
    {
        EnsureOwns(refund);
        refund.RecordOutcome(status, pspReference, errorCode, errorMessage, now);
        RecalculatePaymentStatus();
    }

    public void AddProviderResponse(OrderRefund refund, ProviderResponseRecord response)
    {
        EnsureOwns(refund);
        refund.AddProviderResponse(response);
    }

    private void RecalculatePaymentStatus()
    {
        if (AuthorisedPayment is not null)
        {
            var refunded = RefundedAmountMinor;
            PaymentStatus = refunded == 0
                ? OrderPaymentStatus.Paid
                : refunded >= PaidAmountMinor ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded;
        }
        else
        {
            PaymentStatus = UnsettledPayment is not null ? OrderPaymentStatus.PaymentPending : OrderPaymentStatus.AwaitingPayment;
        }
    }

    private void EnsureOwns(PaymentAttempt attempt)
    {
        if (!_paymentAttempts.Contains(attempt))
            throw new InvalidOperationException("The payment attempt does not belong to this order.");
    }

    private void EnsureOwns(OrderRefund refund)
    {
        if (!_refunds.Contains(refund))
            throw new InvalidOperationException("The refund does not belong to this order.");
    }
}

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
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    private readonly List<PaymentAttempt> _paymentAttempts = new List<PaymentAttempt>();
    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    private readonly List<PaymentProviderResponse> _providerResponses = new List<PaymentProviderResponse>();

    // Using List<>.AsReadOnly()
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
    public IReadOnlyCollection<PaymentAttempt> PaymentAttempts => _paymentAttempts.AsReadOnly();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Everything the payment provider returned for this order's payments and refunds, verbatim.</summary>
    public IReadOnlyCollection<PaymentProviderResponse> ProviderResponses => _providerResponses.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public PaymentAttempt? AuthorisedPayment =>
        _paymentAttempts.FirstOrDefault(a => a.Status == PaymentAttemptStatus.Authorised);

    public PaymentAttempt? LatestPaymentAttempt =>
        _paymentAttempts.OrderByDescending(a => a.AttemptNumber).FirstOrDefault();

    /// <summary>Refunds accepted by the provider.</summary>
    public long RefundedMinorUnits =>
        _refunds.Where(r => r.Status == RefundStatus.Received).Sum(r => r.AmountInMinorUnits);

    /// <summary>What can still be given back: the paid amount minus every refund that is accepted, in flight or unknown.</summary>
    public long RefundableMinorUnits =>
        AuthorisedPayment is { } payment
            ? payment.AmountInMinorUnits - _refunds.Where(r => r.ReservesAmount).Sum(r => r.AmountInMinorUnits)
            : 0;

    /// <summary>
    /// Claims a new payment attempt. When <paramref name="settles"/> is given (an attempt whose outcome is unknown),
    /// the new attempt re-sends it with the same idempotency key and reference, so the provider can answer with the
    /// original result instead of charging twice.
    /// </summary>
    public PaymentAttempt StartPaymentAttempt(long amountInMinorUnits, string currency, DateTimeOffset now, PaymentAttempt? settles = null)
    {
        if (AuthorisedPayment is not null)
            throw new PaymentStateException($"Order {Id} is already paid.");
        if (settles is not null && !_paymentAttempts.Contains(settles))
            throw new PaymentStateException("Only an attempt of this order can be settled.");

        var attemptNumber = (_paymentAttempts.Count == 0 ? 0 : _paymentAttempts.Max(a => a.AttemptNumber)) + 1;
        var idempotencyKey = settles?.IdempotencyKey ?? Guid.NewGuid().ToString();
        var reference = settles?.Reference ?? $"eshop-order-{Id}-pay-{attemptNumber}-{ShortUniqueSuffix()}";
        settles?.MarkSuperseded(now);

        var attempt = new PaymentAttempt(Id, attemptNumber, idempotencyKey, reference, Total(),
            amountInMinorUnits, currency, now, settles?.AttemptNumber);
        _paymentAttempts.Add(attempt);
        RecalculatePaymentStatus();
        return attempt;
    }

    public void RecordPaymentOutcome(PaymentAttempt attempt, PaymentAttemptStatus status, string? pspReference,
        string? resultCode, string? refusalReason, string? refusalReasonCode, string? errorCode, string? errorMessage,
        IEnumerable<CapturedProviderResponse> responses, DateTimeOffset now)
    {
        if (!_paymentAttempts.Contains(attempt))
            throw new PaymentStateException("The attempt does not belong to this order.");
        if (status == PaymentAttemptStatus.Authorised && AuthorisedPayment is { } other && other != attempt)
            throw new PaymentStateException($"Order {Id} already has an authorised payment.");

        attempt.Complete(status, pspReference, resultCode, refusalReason, refusalReasonCode, errorCode, errorMessage, now);
        AddProviderResponses(PaymentProviderResponse.PaymentOperation, attempt.AttemptNumber, null, responses);
        RecalculatePaymentStatus();
    }

    /// <summary>
    /// Claims a refund. Throws when the order is not paid or the amount exceeds what can still be given back.
    /// </summary>
    public OrderRefund StartRefund(decimal amount, long amountInMinorUnits, RefundReason? reason, string? clientRequestKey, DateTimeOffset now)
    {
        var payment = AuthorisedPayment ?? throw new PaymentStateException($"Order {Id} has no payment to refund.");
        if (amountInMinorUnits <= 0)
            throw new PaymentStateException("A refund must be for a positive amount.");
        if (amountInMinorUnits > RefundableMinorUnits)
            throw new PaymentStateException($"Order {Id} cannot be refunded beyond what was paid.");

        var sequence = (_refunds.Count == 0 ? 0 : _refunds.Max(r => r.Sequence)) + 1;
        var refund = new OrderRefund(Id, sequence, Guid.NewGuid(), Guid.NewGuid().ToString(), clientRequestKey,
            $"eshop-order-{Id}-refund-{sequence}-{ShortUniqueSuffix()}", amount, amountInMinorUnits, payment.Currency,
            reason?.ToString(), payment.PspReference!, now);
        _refunds.Add(refund);
        RecalculatePaymentStatus();
        return refund;
    }

    public void RecordRefundOutcome(OrderRefund refund, RefundStatus status, string? pspReference, string? errorCode,
        string? errorMessage, IEnumerable<CapturedProviderResponse> responses, DateTimeOffset now)
    {
        if (!_refunds.Contains(refund))
            throw new PaymentStateException("The refund does not belong to this order.");

        refund.Complete(status, pspReference, errorCode, errorMessage, now);
        AddProviderResponses(PaymentProviderResponse.RefundOperation, null, refund.Sequence, responses);
        RecalculatePaymentStatus();
    }

    private void AddProviderResponses(string operation, int? attemptNumber, int? refundSequence,
        IEnumerable<CapturedProviderResponse> responses)
    {
        foreach (var response in responses)
        {
            _providerResponses.Add(new PaymentProviderResponse(Id, operation, attemptNumber, refundSequence,
                response.ReceivedAt, response.HttpStatus, response.Body, response.Note));
        }
    }

    private void RecalculatePaymentStatus()
    {
        if (AuthorisedPayment is { } payment)
        {
            var refunded = RefundedMinorUnits;
            PaymentStatus = refunded >= payment.AmountInMinorUnits ? OrderPaymentStatus.Refunded
                : refunded > 0 ? OrderPaymentStatus.PartiallyRefunded
                : OrderPaymentStatus.Paid;
            return;
        }

        PaymentStatus = LatestPaymentAttempt?.Status switch
        {
            PaymentAttemptStatus.InFlight or PaymentAttemptStatus.Unknown or PaymentAttemptStatus.Pending
                or PaymentAttemptStatus.PartiallyAuthorised => OrderPaymentStatus.PaymentProcessing,
            _ => OrderPaymentStatus.AwaitingPayment
        };
    }

    private static string ShortUniqueSuffix() => Guid.NewGuid().ToString("N").Substring(0, 8);
}

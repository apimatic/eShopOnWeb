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

    // ---------------------------------------------------------------------
    // Payment / fulfilment state (additive to the historic catalog/order flow).
    // The order carries enough of the state PayPal owns (ids + current status for
    // the hold, the capture, and the refunds) that a later request can act on it.
    // ---------------------------------------------------------------------

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>ISO-4217 currency the order is priced and charged in (from configuration).</summary>
    public string? Currency { get; private set; }

    /// <summary>
    /// The globally-unique reference sent to PayPal as <c>invoice_id</c>/<c>custom_id</c> for this order.
    /// It is the linkage key used by reconciliation and is unique per order (the bare order id is not,
    /// since the in-memory store restarts numbering each run and PayPal blocks duplicate invoice ids).
    /// </summary>
    public string? PayPalInvoiceReference { get; private set; }

    // The hold (authorization)
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // The capture (settlement at fulfilment)
    public string? PayPalCaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public void SetCurrency(string currency)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Currency = currency;
    }

    /// <summary>Record that PayPal is holding the funds for this order.</summary>
    public void MarkAuthorized(string invoiceReference, string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(invoiceReference, nameof(invoiceReference));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        if (PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new PaymentOperationException($"Order {Id} cannot be authorized from state {PaymentStatus}.");
        }

        PayPalInvoiceReference = invoiceReference;
        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    /// <summary>A stale authorization was renewed; the authorization id may have changed.</summary>
    public void MarkReauthorized(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentOperationException($"Order {Id} cannot be reauthorized from state {PaymentStatus}.");
        }

        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>The authorization was captured at fulfilment; record what PayPal reported.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal grossAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentOperationException($"Order {Id} cannot be captured from state {PaymentStatus}.");
        }

        PayPalCaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        PaymentStatus = OrderPaymentStatus.Paid;
    }

    /// <summary>The hold was released before fulfilment; no money moved.</summary>
    public void MarkCancelled()
    {
        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentOperationException($"Order {Id} cannot be cancelled from state {PaymentStatus}. Cancellation only releases a hold that has not been captured.");
        }

        AuthorizationStatus = "VOIDED";
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    /// <summary>Total of refunds that count against the captured amount.</summary>
    public decimal TotalRefunded() => _refunds.Where(r => r.CountsAgainstCapture).Sum(r => r.Amount);

    /// <summary>How much of the capture is still refundable.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>
    /// Guards a refund request against the domain invariants before it is sent to PayPal:
    /// the order must be captured, and the requested amount may never exceed what remains refundable.
    /// </summary>
    public void EnsureRefundable(decimal amount)
    {
        if (PaymentStatus != OrderPaymentStatus.Paid && PaymentStatus != OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentOperationException($"Order {Id} cannot be refunded from state {PaymentStatus}. Only a captured payment can be refunded.");
        }
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (amount > RefundableRemaining())
        {
            throw new PaymentOperationException(
                $"Refund of {amount:0.00} exceeds the {RefundableRemaining():0.00} remaining refundable on order {Id} (captured {CapturedAmount:0.00}, already refunded {TotalRefunded():0.00}).");
        }
    }

    /// <summary>Attach a completed/pending refund and roll the order into Partially/Fully refunded.</summary>
    public void AddRefund(OrderRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        RecalculateRefundState();
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    private void RecalculateRefundState()
    {
        if (PaymentStatus != OrderPaymentStatus.Paid &&
            PaymentStatus != OrderPaymentStatus.PartiallyRefunded &&
            PaymentStatus != OrderPaymentStatus.Refunded)
        {
            return;
        }

        var refunded = TotalRefunded();
        var captured = CapturedAmount ?? 0m;
        if (refunded <= 0m)
        {
            PaymentStatus = OrderPaymentStatus.Paid;
        }
        else if (refunded >= captured)
        {
            PaymentStatus = OrderPaymentStatus.Refunded;
        }
        else
        {
            PaymentStatus = OrderPaymentStatus.PartiallyRefunded;
        }
    }
}

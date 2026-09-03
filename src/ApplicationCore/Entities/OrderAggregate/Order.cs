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
        PaymentReference = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }

    /// <summary>
    /// A unique-per-order reference generated at creation. It seeds a unique PayPal invoice id and the
    /// deterministic PayPal request ids for this order's authorize/create calls — so the same order's
    /// requests are safe to repeat (a double-click), yet never collide with another order (even across
    /// runs of the in-memory database, where the integer id resets).
    /// </summary>
    public string PaymentReference { get; private set; }

    /// <summary>The merchant invoice id sent to PayPal for this order, used to reconcile PayPal's records.</summary>
    public string PayPalInvoiceId => $"ESHOP-{Id}-{PaymentReference.Substring(0, 8)}";
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

    // ---- PayPal payment / fulfilment state (additive) ----

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>ISO-4217 currency the order is (and will be) charged in, captured at authorization time.</summary>
    public string? Currency { get; private set; }

    /// <summary>PayPal Orders v2 order id created for the hold.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>PayPal authorization id (the hold). Updated when a stale authorization is re-authorized.</summary>
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>PayPal capture id (taken at fulfilment).</summary>
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }

    /// <summary>Amount PayPal actually captured.</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>PayPal's fee reported on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant reported on the capture.</summary>
    public decimal? NetAmount { get; private set; }

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Records the PayPal order + authorization that now holds the funds.</summary>
    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Currency = currency;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void MarkAuthorizationFailed()
    {
        PaymentStatus = OrderPaymentStatus.AuthorizationFailed;
    }

    /// <summary>Replaces the authorization after a stale one is renewed (re-authorized).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records the capture taken at fulfilment, with PayPal's reported fee and net proceeds.</summary>
    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void Cancel()
    {
        PaymentStatus = OrderPaymentStatus.Cancelled;
        AuthorizationStatus = "VOIDED";
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>Amount still refundable: captured minus already refunded. Zero if not yet captured.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>Returns the refund previously recorded under the same idempotency key, if any.</summary>
    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>Adds a refund and advances the payment status to partially/fully refunded.</summary>
    public void AddRefund(OrderRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        PaymentStatus = TotalRefunded() >= (CapturedAmount ?? 0m)
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
    }
}

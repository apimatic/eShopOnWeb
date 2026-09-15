using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Order() { }
#pragma warning restore CS8618

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
        : this(buyerId, shipToAddress, items, null)
    {
    }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string? paymentCurrency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        PaymentCurrency = paymentCurrency;
        PaymentStatus = paymentCurrency is null ? OrderPaymentStatus.NotRequired : OrderPaymentStatus.AwaitingPayment;
        PaymentReference = paymentCurrency is null ? null : Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderPaymentStatus PaymentStatus { get; private set; }
    public OrderFulfilmentStatus FulfilmentStatus { get; private set; }
    public string? PaymentCurrency { get; private set; }
    public string? PaymentReference { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount { get; private set; }

    private readonly List<OrderItem> _orderItems = new();
    private readonly List<PaymentAuthorization> _paymentAuthorizations = new();
    private readonly List<PaymentRefund> _paymentRefunds = new();

    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
    public IReadOnlyCollection<PaymentAuthorization> PaymentAuthorizations => _paymentAuthorizations.AsReadOnly();
    public IReadOnlyCollection<PaymentRefund> PaymentRefunds => _paymentRefunds.AsReadOnly();
    public PaymentAuthorization? CurrentAuthorization => _paymentAuthorizations.SingleOrDefault(x => x.IsCurrent);

    public decimal Total() => _orderItems.Sum(item => item.UnitPrice * item.Units);

    public void RecordAuthorization(string paypalOrderId, string authorizationId, string status,
        decimal amount, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        if (PaymentStatus != OrderPaymentStatus.AwaitingPayment)
            throw new InvalidOperationException("Only an order awaiting payment can be authorized.");
        if (amount != Total())
            throw new InvalidOperationException("PayPal authorized an amount that does not equal the order total.");

        PayPalOrderId = paypalOrderId;
        _paymentAuthorizations.Add(new PaymentAuthorization(authorizationId, status, amount, createdAt, expiresAt, true));
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string status, decimal amount,
        DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        if (PaymentStatus != OrderPaymentStatus.Authorized)
            throw new InvalidOperationException("Only an authorized order can be reauthorized.");
        if (amount != Total())
            throw new InvalidOperationException("PayPal reauthorized an amount that does not equal the order total.");

        foreach (var authorization in _paymentAuthorizations.Where(x => x.IsCurrent))
            authorization.MakeHistorical();

        _paymentAuthorizations.Add(new PaymentAuthorization(authorizationId, status, amount, createdAt, expiresAt, true));
    }

    public void RecordCapture(string captureId, string status, decimal amount, decimal? fee,
        decimal? netProceeds, DateTimeOffset capturedAt)
    {
        if (PaymentStatus is not (OrderPaymentStatus.Authorized or OrderPaymentStatus.CapturePending))
            throw new InvalidOperationException("Only an authorized order can be fulfilled and captured.");
        if (amount != Total())
            throw new InvalidOperationException("PayPal captured an amount that does not equal the order total.");

        CurrentAuthorization?.UpdateStatus("CAPTURED");
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetProceeds = netProceeds;
        CapturedAt = capturedAt;
        if (string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            PaymentStatus = OrderPaymentStatus.Captured;
            FulfilmentStatus = OrderFulfilmentStatus.Fulfilled;
        }
        else
        {
            PaymentStatus = OrderPaymentStatus.CapturePending;
        }
    }

    public void CancelWithoutAuthorization()
    {
        if (PaymentStatus != OrderPaymentStatus.AwaitingPayment || FulfilmentStatus != OrderFulfilmentStatus.Unfulfilled)
            throw new InvalidOperationException("Only an unfulfilled order awaiting payment can be cancelled without a void.");

        PaymentStatus = OrderPaymentStatus.Voided;
        FulfilmentStatus = OrderFulfilmentStatus.Cancelled;
    }

    public void RecordVoid(string status)
    {
        if (PaymentStatus != OrderPaymentStatus.Authorized || FulfilmentStatus != OrderFulfilmentStatus.Unfulfilled)
            throw new InvalidOperationException("Only an authorized, unfulfilled order can be voided.");

        CurrentAuthorization?.UpdateStatus(status);
        PaymentStatus = OrderPaymentStatus.Voided;
        FulfilmentStatus = OrderFulfilmentStatus.Cancelled;
    }

    public PaymentRefund? FindRefund(string idempotencyKey) =>
        _paymentRefunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);

    public decimal RefundableAmount() => (CapturedAmount ?? 0m) - RefundedAmount;

    public void RecordRefund(string refundId, string idempotencyKey, string status, decimal amount,
        DateTimeOffset createdAt)
    {
        if (PaymentStatus is not (OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded))
            throw new InvalidOperationException("Only a captured payment can be refunded.");
        if (amount <= 0 || amount > RefundableAmount())
            throw new InvalidOperationException("The refund exceeds the remaining captured amount.");

        _paymentRefunds.Add(new PaymentRefund(refundId, idempotencyKey, status, amount, createdAt));
        RefundedAmount += amount;
        PaymentStatus = RefundedAmount == CapturedAmount
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
    }
}

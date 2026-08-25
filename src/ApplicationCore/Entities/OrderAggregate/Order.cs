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
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        PaymentStatus = PaymentStatus.PendingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.PendingPayment;

    // PayPal payment tracking (stored as strings to preserve exact PayPal precision)
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CapturedAmount { get; private set; }
    public string? PayPalFee { get; private set; }
    public string? NetAmount { get; private set; }

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public void SetPayPalOrderId(string paypalOrderId)
    {
        PayPalOrderId = paypalOrderId;
    }

    public void Authorize(string authorizationId, string? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = PaymentStatus.Authorized;
    }

    public void UpdateAuthorization(string newAuthorizationId, string? newExpiresAt)
    {
        AuthorizationId = newAuthorizationId;
        AuthorizationExpiresAt = newExpiresAt;
    }

    public void Fulfil(string captureId, string? capturedAmount, string? payPalFee, string? netAmount)
    {
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        PaymentStatus = PaymentStatus.Fulfilled;
    }

    public void Cancel()
    {
        PaymentStatus = PaymentStatus.Cancelled;
        AuthorizationId = null;
        AuthorizationExpiresAt = null;
    }

    public void AddRefund(OrderRefund refund)
    {
        _refunds.Add(refund);
        if (decimal.TryParse(CapturedAmount, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var captured)
            && TotalRefunded >= captured)
        {
            PaymentStatus = PaymentStatus.Refunded;
        }
    }
}

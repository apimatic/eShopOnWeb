using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

public class OrderPayment : BaseEntity, IAggregateRoot
{
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Negative(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? CaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void SetPayPalOrderCreated(string paypalOrderId)
    {
        PayPalOrderId = paypalOrderId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetAuthorized(string authorizationId)
    {
        AuthorizationId = authorizationId;
        Status = PaymentStatus.Authorized;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateAuthorizationId(string newAuthorizationId)
    {
        AuthorizationId = newAuthorizationId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCaptured(string captureId, decimal capturedAmount, decimal paypalFee, decimal netAmount)
    {
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetVoided()
    {
        Status = PaymentStatus.Voided;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetFailed()
    {
        Status = PaymentStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AddRefund(PaymentRefund refund)
    {
        _refunds.Add(refund);
        var totalRefunded = TotalRefunded();
        Status = totalRefunded >= (CapturedAmount ?? Amount)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public decimal TotalRefunded()
    {
        var total = 0m;
        foreach (var r in _refunds)
            total += r.Amount;
        return total;
    }
}

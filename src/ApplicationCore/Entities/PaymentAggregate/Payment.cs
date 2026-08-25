using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Negative(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.PendingPayment;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public PaymentStatus Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public DateTimeOffset? AuthorizationExpiry { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void RecordAuthorization(string paypalOrderId, string authorizationId, DateTimeOffset? expiry)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = paypalOrderId;
        PayPalAuthorizationId = authorizationId;
        AuthorizationExpiry = expiry;
        Status = PaymentStatus.Authorized;
    }

    public void RecordCapture(string captureId, decimal capturedAmount, decimal paypalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        PayPalCaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
    }

    public void UpdateAuthorizationId(string newAuthorizationId, DateTimeOffset? newExpiry)
    {
        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        PayPalAuthorizationId = newAuthorizationId;
        AuthorizationExpiry = newExpiry;
    }

    public void RecordVoid()
    {
        Status = PaymentStatus.Voided;
    }

    public void RecordRefund(string paypalRefundId, string idempotencyKey, decimal refundAmount, string currency)
    {
        _refunds.Add(new PaymentRefund(paypalRefundId, idempotencyKey, refundAmount, currency));

        var totalRefunded = TotalRefunded();
        if (totalRefunded >= Amount)
        {
            Status = PaymentStatus.Refunded;
        }
        else
        {
            Status = PaymentStatus.PartiallyRefunded;
        }
    }

    public decimal TotalRefunded()
    {
        var total = 0m;
        foreach (var r in _refunds)
            total += r.Amount;
        return total;
    }

    public decimal RemainingRefundable()
    {
        return (CapturedAmount ?? Amount) - TotalRefunded();
    }
}

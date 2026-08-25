using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string payPalOrderId, string authorizationId, string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        OrderId = orderId;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        Currency = currency;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string Currency { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    public void SetAuthorizationCreatedAt(DateTimeOffset createdAt)
    {
        AuthorizationCreatedAt = createdAt;
    }

    public void Reauthorize(string newAuthorizationId)
    {
        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        AuthorizationId = newAuthorizationId;
        AuthorizationCreatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCaptured(string captureId, decimal capturedAmount, decimal paypalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
    }

    public PaymentRefund AddRefund(string paypalRefundId, string idempotencyKey, decimal amount, string currency)
    {
        var refund = new PaymentRefund(Id, paypalRefundId, idempotencyKey, amount, currency);
        _refunds.Add(refund);
        return refund;
    }

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}

using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

// One Payment per Order (1:1), tracked as its own aggregate so the PayPal-owned state
// (authorization, capture, refunds) doesn't leak into the Order aggregate's invariants.
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.AwaitingAuthorization;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public decimal RefundedAmount { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        if (Status != PaymentStatus.AwaitingAuthorization)
        {
            throw new InvalidOperationException($"Cannot record an authorization for a payment in status {Status}.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal? feeAmount, decimal? netAmount)
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException($"Cannot record a capture for a payment in status {Status}.");
        }

        PayPalCaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFeeAmount = feeAmount;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    public void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public Refund? GetRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
    }

    public Refund AddRefund(string idempotencyKey, string payPalRefundId, string status, decimal amount)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Cannot refund a payment in status {Status}.");
        }

        var existing = GetRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var refund = new Refund(idempotencyKey, payPalRefundId, status, amount, DateTimeOffset.UtcNow);
        _refunds.Add(refund);
        RefundedAmount += amount;
        Status = RefundedAmount >= CapturedAmount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}

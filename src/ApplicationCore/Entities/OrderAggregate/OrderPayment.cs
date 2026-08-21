using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    private readonly List<OrderRefund> _refunds = new();

    public int OrderId { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? Currency { get; private set; }
    public string? AuthorizeRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }
    public string? VoidRequestId { get; private set; }

    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public void EnsureAuthorizeRequestId(string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        AuthorizeRequestId ??= idempotencyKey;
    }

    public void EnsureCaptureRequestId(string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        CaptureRequestId ??= idempotencyKey;
    }

    public void EnsureVoidRequestId(string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        VoidRequestId ??= idempotencyKey;
    }

    public void ApplyAuthorization(
        string payPalOrderId,
        string? payPalOrderStatus,
        string authorizationId,
        string? authorizationStatus,
        DateTimeOffset? expiration,
        DateTimeOffset? createdAt,
        string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        AuthorizationCreatedAt = createdAt;
        Currency = currency;
    }

    public void ReplaceAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
    }

    public void ApplyCapture(
        string captureId,
        string? captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        string? payPalOrderStatus)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.Negative(capturedAmount, nameof(capturedAmount));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        PayPalOrderStatus = payPalOrderStatus ?? PayPalOrderStatus;
    }

    public void ApplyVoid(string? authorizationStatus, string? payPalOrderStatus)
    {
        AuthorizationStatus = authorizationStatus;
        PayPalOrderStatus = payPalOrderStatus ?? PayPalOrderStatus;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
    }

    public void AddRefund(OrderRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
    }

    public decimal RefundedTotal()
    {
        return _refunds
            .Where(r => !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.Amount);
    }

    public decimal RemainingRefundable()
    {
        if (CapturedAmount is null)
        {
            return 0;
        }

        var remaining = CapturedAmount.Value - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }
}

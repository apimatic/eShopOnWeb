using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        OrderId = orderId;
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => _refunds.Sum(r => r.Amount);

    public decimal RemainingRefundable
    {
        get
        {
            var captured = CapturedAmount ?? 0m;
            var remaining = captured - RefundedAmount;
            return remaining < 0 ? 0 : remaining;
        }
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string key) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, key, StringComparison.Ordinal));

    public void RecordAuthorization(
        string paypalOrderId,
        string paypalOrderStatus,
        string authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));

        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = paypalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        AuthorizationExpiration = expiration;
        AuthorizedAt = DateTimeOffset.UtcNow;
        VoidedAt = null;
    }

    public void RefreshAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NegativeOrZero(capturedAmount, nameof(capturedAmount));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid(string? authorizationStatus)
    {
        AuthorizationStatus = string.IsNullOrWhiteSpace(authorizationStatus) ? "VOIDED" : authorizationStatus;
        VoidedAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund RecordRefund(string paypalRefundId, string status, decimal amount, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(paypalRefundId, nameof(paypalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        if (amount > RemainingRefundable)
        {
            throw new InvalidOperationException(
                $"Refund of {amount:0.00} {Currency} exceeds the remaining refundable amount of {RemainingRefundable:0.00} {Currency}.");
        }

        var refund = new PaymentRefund(paypalRefundId, status, amount, Currency, idempotencyKey);
        _refunds.Add(refund);
        if (!string.IsNullOrEmpty(CaptureStatus))
        {
            CaptureStatus = RemainingRefundable == 0 ? "REFUNDED" : "PARTIALLY_REFUNDED";
        }

        return refund;
    }
}

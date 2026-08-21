using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderPayment() { }
    #pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency;
        Status = OrderPaymentStatus.AwaitingAuthorization;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public OrderPaymentStatus Status { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? InvoiceId { get; private set; }

    public string? PayPalAuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => _refunds
        .Where(r => !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
        .Sum(r => r.Amount);

    public decimal RemainingRefundableAmount
    {
        get
        {
            var captured = CapturedAmount ?? 0m;
            var remaining = captured - RefundedAmount;
            return remaining < 0 ? 0 : decimal.Round(remaining, 2, MidpointRounding.AwayFromZero);
        }
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public void RecordPayPalOrder(string payPalOrderId, string? status, string? invoiceId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = status;
        if (!string.IsNullOrWhiteSpace(invoiceId))
        {
            InvoiceId = invoiceId;
        }
    }

    public void RecordAuthorization(
        string authorizationId,
        string? status,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = expiresAt;
        Status = OrderPaymentStatus.Authorized;
    }

    public void RecordCapture(
        string captureId,
        string? status,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        DateTimeOffset? capturedAt)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        PayPalCaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = decimal.Round(capturedAmount, 2, MidpointRounding.AwayFromZero);
        PayPalFee = paypalFee.HasValue ? decimal.Round(paypalFee.Value, 2, MidpointRounding.AwayFromZero) : null;
        NetAmount = netAmount.HasValue ? decimal.Round(netAmount.Value, 2, MidpointRounding.AwayFromZero) : null;
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow;
        Status = OrderPaymentStatus.Captured;
    }

    public void RecordVoid(string? authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus ?? "VOIDED";
        Status = OrderPaymentStatus.Voided;
    }

    public PaymentRefund RecordRefund(
        string payPalRefundId,
        string idempotencyKey,
        decimal amount,
        string status,
        DateTimeOffset? createdAt)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var refund = new PaymentRefund(payPalRefundId, idempotencyKey, amount, Currency, status, createdAt ?? DateTimeOffset.UtcNow);
        _refunds.Add(refund);

        if (RemainingRefundableAmount == 0)
        {
            Status = OrderPaymentStatus.Refunded;
        }
        else
        {
            Status = OrderPaymentStatus.PartiallyRefunded;
        }

        return refund;
    }
}

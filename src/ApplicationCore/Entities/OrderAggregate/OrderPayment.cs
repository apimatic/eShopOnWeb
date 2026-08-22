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

    public OrderPayment(int orderId, string currency, string invoiceId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));

        OrderId = orderId;
        Currency = currency;
        InvoiceId = invoiceId;
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; }
    public string InvoiceId { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? OriginalAuthorizationAt { get; private set; }
    public decimal AuthorizedAmount { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedTotal =>
        _refunds.Where(r => r.IsSuccessful).Sum(r => r.Amount);

    public decimal RefundableRemaining
    {
        get
        {
            var captured = CapturedAmount ?? 0m;
            var remaining = captured - RefundedTotal;
            return remaining < 0 ? 0 : remaining;
        }
    }

    public void RecordPayPalOrder(string paypalOrderId, string? status)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = status;
    }

    public void RecordAuthorization(
        string authorizationId,
        string status,
        decimal amount,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        OriginalAuthorizationAt ??= createdAt;
        PayPalOrderStatus = "COMPLETED";
    }

    public void RecordReauthorization(
        string authorizationId,
        string status,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        DateTimeOffset capturedAt)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NegativeOrZero(capturedAmount, nameof(capturedAmount));

        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid(string? status)
    {
        AuthorizationStatus = string.IsNullOrWhiteSpace(status) ? "VOIDED" : status;
        PayPalOrderStatus = "VOIDED";
    }

    public OrderRefund AddRefund(string paypalRefundId, string status, decimal amount, string idempotencyKey)
    {
        var refund = new OrderRefund(paypalRefundId, status, amount, Currency, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
}

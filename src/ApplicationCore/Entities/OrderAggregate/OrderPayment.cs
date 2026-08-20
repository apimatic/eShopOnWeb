using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
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
    public string? InvoiceId { get; private set; }
    public string? GatewayRequestId { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => _refunds.Sum(r => r.Amount);
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - RefundedAmount;

    public OrderRefund? FindRefundByIdempotencyKey(string key) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, key, StringComparison.Ordinal));

    public void RecordPayPalOrder(string paypalOrderId, string status, string? invoiceId = null)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = status;
        if (!string.IsNullOrWhiteSpace(invoiceId))
        {
            InvoiceId = invoiceId;
        }
    }

    public string EnsureInvoiceId()
    {
        if (string.IsNullOrWhiteSpace(InvoiceId))
        {
            InvoiceId = $"ESHOP-{OrderId}-{Guid.NewGuid():N}";
            if (InvoiceId.Length > 127)
            {
                InvoiceId = InvoiceId[..127];
            }
        }

        return InvoiceId;
    }

    public string EnsureGatewayRequestId()
    {
        if (string.IsNullOrWhiteSpace(GatewayRequestId))
        {
            GatewayRequestId = Guid.NewGuid().ToString("N");
        }

        return GatewayRequestId;
    }

    public void RecordAuthorization(
        string authorizationId,
        string status,
        decimal amount,
        DateTimeOffset? expiration,
        DateTimeOffset authorizedAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationExpiration = expiration;
        AuthorizedAt = authorizedAt;
        PayPalOrderStatus = "COMPLETED";
    }

    public void RecordReauthorization(
        string authorizationId,
        string status,
        decimal amount,
        DateTimeOffset? expiration,
        DateTimeOffset authorizedAt)
    {
        RecordAuthorization(authorizationId, status, amount, expiration, authorizedAt);
    }

    public void RecordCapture(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netProceeds,
        DateTimeOffset capturedAt)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NegativeOrZero(capturedAmount, nameof(capturedAmount));

        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetProceeds = netProceeds;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid(string status)
    {
        AuthorizationStatus = status;
        PayPalOrderStatus = "VOIDED";
    }

    public OrderRefund AddRefund(string paypalRefundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(paypalRefundId, nameof(paypalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (amount - RefundableRemaining > 0.001m)
        {
            throw new InvalidOperationException(
                $"Refund of {amount} exceeds the remaining captured amount of {RefundableRemaining}.");
        }

        var refund = new OrderRefund(paypalRefundId, amount, Currency, status, idempotencyKey);
        _refunds.Add(refund);
        CaptureStatus = RefundableRemaining <= 0.001m ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return refund;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment
{
#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(string paypalOrderId, string currency, string? invoiceId = null)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        PayPalOrderId = paypalOrderId;
        Currency = currency;
        InvoiceId = invoiceId;
    }

    public string PayPalOrderId { get; private set; }
    public string Currency { get; private set; }
    public string? InvoiceId { get; private set; }

    public string? OriginalAuthorizationId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? OriginalAuthorizationTime { get; private set; }
    public DateTimeOffset? AuthorizationCreateTime { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => decimal.Round(_refunds.Sum(r => r.Amount), 2, MidpointRounding.AwayFromZero);

    public decimal RefundableRemaining
    {
        get
        {
            var captured = CapturedAmount ?? 0m;
            var remaining = captured - TotalRefunded;
            return remaining < 0 ? 0 : decimal.Round(remaining, 2, MidpointRounding.AwayFromZero);
        }
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public void RecordAuthorization(
        string authorizationId,
        string status,
        DateTimeOffset? createTime,
        DateTimeOffset? expirationTime)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationCreateTime = createTime;
        AuthorizationExpirationTime = expirationTime;
        OriginalAuthorizationId ??= authorizationId;
        OriginalAuthorizationTime ??= createTime ?? DateTimeOffset.UtcNow;
    }

    public void SetInvoiceId(string invoiceId)
    {
        if (!string.IsNullOrWhiteSpace(invoiceId))
        {
            InvoiceId = invoiceId;
        }
    }

    public void RecordReauthorization(
        string authorizationId,
        string status,
        DateTimeOffset? createTime,
        DateTimeOffset? expirationTime)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationCreateTime = createTime;
        AuthorizationExpirationTime = expirationTime;
    }

    public void RecordCapture(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal paypalFee,
        decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = decimal.Round(capturedAmount, 2, MidpointRounding.AwayFromZero);
        PaypalFee = decimal.Round(paypalFee, 2, MidpointRounding.AwayFromZero);
        NetAmount = decimal.Round(netAmount, 2, MidpointRounding.AwayFromZero);
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid(string status)
    {
        AuthorizationStatus = status;
    }

    public OrderRefund RecordRefund(string paypalRefundId, string status, decimal amount, string idempotencyKey)
    {
        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var refund = new OrderRefund(paypalRefundId, status, amount, Currency, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }
}

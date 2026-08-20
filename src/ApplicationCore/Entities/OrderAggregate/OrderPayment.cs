using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string currency)
    {
        OrderId = orderId;
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? OriginalAuthorizedAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public string? CardLast4 { get; private set; }
    public string? CardBrand { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => _refunds
        .Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        .Sum(r => r.Amount);

    public decimal RefundableRemaining =>
        Math.Max(0, (CapturedAmount ?? 0) - RefundedAmount);

    public OrderRefund? FindRefundByIdempotencyKey(string key) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == key);

    public void EnsureIdempotencyKey()
    {
        IdempotencyKey ??= Guid.NewGuid().ToString("N");
    }

    public void SetPayPalOrderId(string paypalOrderId)
    {
        PayPalOrderId = paypalOrderId;
    }

    public void RecordAuthorization(
        string paypalOrderId,
        string authorizationId,
        string status,
        DateTimeOffset? expiration,
        DateTimeOffset authorizedAt,
        string? last4,
        string? brand,
        int? savedPaymentMethodId)
    {
        PayPalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
        AuthorizedAt = authorizedAt;
        OriginalAuthorizedAt ??= authorizedAt;
        CardLast4 = last4 ?? CardLast4;
        CardBrand = brand ?? CardBrand;
        if (savedPaymentMethodId.HasValue)
        {
            SavedPaymentMethodId = savedPaymentMethodId;
        }
    }

    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset? expiration, DateTimeOffset authorizedAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
        AuthorizedAt = authorizedAt;
    }

    public void RecordCapture(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        DateTimeOffset capturedAt)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid(string? authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus ?? "VOIDED";
    }

    public OrderRefund AddRefund(string paypalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        var refund = new OrderRefund(paypalRefundId, status, amount, currency, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }
}

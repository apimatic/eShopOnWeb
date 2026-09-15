using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment
{
    public string? PayPalOrderId { get; private set; }
    public string? OriginalAuthorizationId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? OriginalAuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? HonorPeriodEndsAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public string? Currency { get; private set; }

    public List<OrderRefund> Refunds { get; private set; } = new();

    public decimal RemainingRefundableAmount()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedAmount;
        return remaining < 0 ? 0 : remaining;
    }

    internal void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string status,
        DateTimeOffset authorizedAt,
        DateTimeOffset? expiresAt,
        string currency)
    {
        PayPalOrderId = payPalOrderId;
        OriginalAuthorizationId = authorizationId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAt = authorizedAt;
        OriginalAuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
        HonorPeriodEndsAt = authorizedAt.AddDays(3);
        Currency = currency;
    }

    internal void RecordReauthorization(
        string authorizationId,
        string status,
        DateTimeOffset authorizedAt,
        DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
        HonorPeriodEndsAt = authorizedAt.AddDays(3);
    }

    internal void RecordCapture(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal? payPalFee,
        decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
    }

    internal void RecordVoid(string status)
    {
        AuthorizationStatus = status;
    }

    internal OrderRefund AddRefund(string idempotencyKey, string payPalRefundId, string status, decimal amount)
    {
        var refund = new OrderRefund(idempotencyKey, payPalRefundId, status, amount);
        Refunds.Add(refund);
        RefundedAmount += amount;
        return refund;
    }

    internal OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        foreach (var refund in Refunds)
        {
            if (string.Equals(refund.IdempotencyKey, idempotencyKey, StringComparison.Ordinal))
            {
                return refund;
            }
        }

        return null;
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// PayPal-owned identifiers and amounts for an order payment. Card PANs are never stored here.
/// </summary>
public class OrderPayment
{
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public string Currency { get; private set; } = string.Empty;
    public decimal RefundedAmount { get; private set; }

    public decimal RemainingRefundableAmount
    {
        get
        {
            var captured = CapturedAmount ?? 0m;
            var remaining = captured - RefundedAmount;
            return remaining < 0m ? 0m : remaining;
        }
    }

    internal void RecordAuthorization(
        string payPalOrderId,
        string payPalOrderStatus,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt,
        string currency)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        Currency = currency;
    }

    internal void RecordReauthorization(
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
    }

    internal void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? payPalFee,
        decimal? netAmount,
        DateTimeOffset? capturedAt,
        string? authorizationStatus)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        if (!string.IsNullOrWhiteSpace(authorizationStatus))
        {
            AuthorizationStatus = authorizationStatus;
        }
    }

    internal void RecordVoid(string authorizationStatus, string? payPalOrderStatus)
    {
        AuthorizationStatus = authorizationStatus;
        if (!string.IsNullOrWhiteSpace(payPalOrderStatus))
        {
            PayPalOrderStatus = payPalOrderStatus;
        }
    }

    internal void AddRefundedAmount(decimal amount)
    {
        RefundedAmount += amount;
    }
}

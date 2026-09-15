using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment
{
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public string? Currency { get; private set; }
    public string PaymentAttemptKey { get; private set; } = Guid.NewGuid().ToString("N");

    public void RecordPayPalOrder(string payPalOrderId, string status)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = status;
    }

    public void RecordAuthorization(
        string authorizationId,
        string status,
        decimal amount,
        string currency,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        Currency = currency;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        string currency)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        Currency = currency;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
        PayPalOrderStatus = "VOIDED";
    }

    public void UpdateCaptureStatus(string status)
    {
        CaptureStatus = status;
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentDetails
{
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal CapturedAmount { get; private set; }
    public decimal PayPalFee { get; private set; }
    public decimal NetAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset? CapturedAt { get; private set; }

    public static PaymentDetails FromAuthorization(
        string paypalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? createdAt,
        DateTimeOffset? expirationTime,
        string currency)
    {
        return new PaymentDetails
        {
            PayPalOrderId = paypalOrderId,
            AuthorizationId = authorizationId,
            AuthorizationStatus = authorizationStatus,
            AuthorizationCreatedAt = createdAt,
            AuthorizationExpirationTime = expirationTime,
            Currency = currency
        };
    }

    public void UpdateAuthorization(string authorizationId, string status, DateTimeOffset? createdAt, DateTimeOffset? expirationTime)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        if (createdAt.HasValue)
        {
            AuthorizationCreatedAt = createdAt;
        }
        AuthorizationExpirationTime = expirationTime;
    }

    public void ApplyCapture(string captureId, string captureStatus, decimal capturedAmount, decimal paypalFee, decimal netAmount, DateTimeOffset capturedAt)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
    }
}

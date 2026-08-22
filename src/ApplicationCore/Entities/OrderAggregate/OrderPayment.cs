using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment
{
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public string? Currency { get; private set; }

    internal void SetConfiguredCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(Currency))
        {
            Currency = currency;
        }
    }

    internal void RecordAuthorization(
        string paypalOrderId,
        string paypalOrderStatus,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationCreatedAt,
        DateTimeOffset? authorizationExpiration,
        string currency)
    {
        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = paypalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = authorizationCreatedAt;
        AuthorizationExpiration = authorizationExpiration;
        Currency = currency;
    }

    internal void UpdateAuthorization(
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationCreatedAt,
        DateTimeOffset? authorizationExpiration)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = authorizationCreatedAt;
        AuthorizationExpiration = authorizationExpiration;
    }

    internal void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal paypalFee,
        decimal netProceeds,
        string? captureCurrency = null)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetProceeds = netProceeds;
        if (!string.IsNullOrWhiteSpace(captureCurrency))
        {
            Currency = captureCurrency;
        }
    }

    internal void UpdateCaptureStatus(string captureStatus)
    {
        CaptureStatus = captureStatus;
    }

    internal void UpdateAuthorizationStatus(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
    }

    internal void UpdatePayPalOrderStatus(string paypalOrderStatus)
    {
        PayPalOrderStatus = paypalOrderStatus;
    }
}

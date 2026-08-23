using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// PayPal-owned identifiers and amounts for an eShop order. Card data is never stored here.
/// </summary>
public class OrderPayment
{
    public string Currency { get; private set; } = string.Empty;
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public void RecordAuthorization(
        string paypalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiration,
        decimal authorizedAmount,
        string currency)
    {
        PayPalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public void RecordReauthorization(
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiration)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
    }
}

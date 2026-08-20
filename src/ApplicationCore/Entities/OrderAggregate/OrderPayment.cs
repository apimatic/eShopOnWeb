using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment
{
#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(
        string paypalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationExpiration,
        decimal authorizedAmount,
        string currency)
    {
        PayPalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = authorizationExpiration;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
    }

    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public string Currency { get; private set; }

    public void UpdateAuthorization(string authorizationId, string status, DateTimeOffset? expiration)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal paypalFee, decimal netProceeds)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetProceeds = netProceeds;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid(string status)
    {
        AuthorizationStatus = status;
    }
}

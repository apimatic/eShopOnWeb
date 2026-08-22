using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// PayPal-owned identifiers and amounts for an eShop order. Card PANs are never stored here.
/// </summary>
public class OrderPayment
{
#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(string payPalOrderId, string currency, string? payPalRequestId)
    {
        PayPalOrderId = payPalOrderId;
        Currency = currency;
        PayPalRequestId = payPalRequestId;
    }

    public string PayPalOrderId { get; private set; }
    public string? PayPalRequestId { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public string Currency { get; private set; }

    public string? FulfilRequestId { get; private set; }
    public string? CancelRequestId { get; private set; }

    public int? SavedCardId { get; private set; }

    public void RecordAuthorization(string authorizationId, string status, DateTimeOffset? expiration, DateTimeOffset authorizedAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
        AuthorizedAt = authorizedAt;
    }

    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset? expiration)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
    }

    public void RecordCapture(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netProceeds,
        DateTimeOffset capturedAt,
        string fulfilRequestId)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetProceeds = netProceeds;
        CapturedAt = capturedAt;
        FulfilRequestId = fulfilRequestId;
    }

    public void RecordVoid(string? status, string cancelRequestId)
    {
        AuthorizationStatus = status ?? "VOIDED";
        CancelRequestId = cancelRequestId;
    }

    public void UpdateAuthorizationStatus(string status, DateTimeOffset? expiration)
    {
        AuthorizationStatus = status;
        if (expiration.HasValue)
        {
            AuthorizationExpiration = expiration;
        }
    }

    public void UpdateCaptureStatus(string status)
    {
        CaptureStatus = status;
    }

    public void AssociateSavedCard(int savedCardId)
    {
        SavedCardId = savedCardId;
    }
}

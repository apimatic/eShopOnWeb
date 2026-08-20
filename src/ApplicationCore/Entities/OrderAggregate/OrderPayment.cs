using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// PayPal-owned identifiers and amounts for an eShop order. Card PANs are never stored here.
/// </summary>
public class OrderPayment
{
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? OriginalAuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    public void SetCurrency(string currency)
    {
        Currency = currency;
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string status,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt,
        string? cardBrand,
        string? cardLast4)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt;
        OriginalAuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
    }

    public void ReplaceAuthorization(string authorizationId, string status, DateTimeOffset createdAt, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
    }
}

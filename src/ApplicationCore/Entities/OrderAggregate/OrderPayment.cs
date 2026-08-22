using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// PayPal-owned identifiers and amounts needed to authorize, capture, void, or refund later.
/// Card PAN/CVC are never stored here.
/// </summary>
public class OrderPayment
{
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? OriginalAuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? Currency { get; private set; }
    public string? CardLastDigits { get; private set; }
    public string? CardBrand { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string status,
        decimal amount,
        string currency,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt,
        string? cardLastDigits,
        string? cardBrand)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        OriginalAuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        Currency = currency;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        CardLastDigits = cardLastDigits;
        CardBrand = cardBrand;
    }

    public void RecordReauthorization(string newAuthorizationId, string status, DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        AuthorizationId = newAuthorizationId;
        AuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal paypalFee,
        decimal netAmount,
        DateTimeOffset capturedAt)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid(DateTimeOffset cancelledAt)
    {
        AuthorizationStatus = "VOIDED";
        CancelledAt = cancelledAt;
    }
}

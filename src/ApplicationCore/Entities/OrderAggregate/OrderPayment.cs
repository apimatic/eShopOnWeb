using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment
{
#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(
        string payPalOrderId,
        string invoiceId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset authorizedAt,
        DateTimeOffset? authorizationExpiration,
        string currency)
    {
        PayPalOrderId = payPalOrderId;
        InvoiceId = invoiceId;
        AuthorizationId = authorizationId;
        OriginalAuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiration = authorizationExpiration;
        Currency = currency;
    }

    public string PayPalOrderId { get; private set; }
    public string InvoiceId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string OriginalAuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string Currency { get; private set; }

    public bool HonorPeriodElapsed(TimeSpan honorPeriod) =>
        DateTimeOffset.UtcNow >= AuthorizedAt.Add(honorPeriod);

    public bool IsExpired =>
        AuthorizationExpiration.HasValue && AuthorizationExpiration.Value <= DateTimeOffset.UtcNow;

    public void UpdateAuthorization(
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset authorizedAt,
        DateTimeOffset? authorizationExpiration)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiration = authorizationExpiration;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal payPalFee,
        decimal netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
    }
}

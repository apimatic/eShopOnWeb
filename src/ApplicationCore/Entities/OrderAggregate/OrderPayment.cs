using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment
{
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public DateTimeOffset? OriginalAuthorizationTime { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? Currency { get; private set; }
    public string? InvoiceId { get; private set; }
    public string? CreateRequestId { get; private set; }
    public string? AuthorizeRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }
    public string? VoidRequestId { get; private set; }

    public void EnsureCreateRequestId(string requestId) => CreateRequestId ??= requestId;

    public void EnsureAuthorizeRequestId(string requestId) => AuthorizeRequestId ??= requestId;

    public void EnsureCaptureRequestId(string requestId) => CaptureRequestId ??= requestId;

    public void EnsureVoidRequestId(string requestId) => VoidRequestId ??= requestId;

    public void EnsureInvoiceId(string invoiceId) => InvoiceId ??= invoiceId;

    public void RecordPayPalOrder(string payPalOrderId, string currency, string? invoiceId = null)
    {
        PayPalOrderId = payPalOrderId;
        Currency = currency;
        if (!string.IsNullOrWhiteSpace(invoiceId))
        {
            InvoiceId = invoiceId;
        }
    }

    public void RecordAuthorization(
        string authorizationId,
        string status,
        DateTimeOffset? expiration,
        DateTimeOffset? createTime,
        string currency)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
        OriginalAuthorizationTime ??= createTime;
        Currency = currency;
    }

    public void ReplaceAuthorization(
        string authorizationId,
        string status,
        DateTimeOffset? expiration)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
    }

    public void RecordCapture(
        string captureId,
        string status,
        decimal? capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        string? currency)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        if (!string.IsNullOrWhiteSpace(currency))
        {
            Currency = currency;
        }
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid(string status)
    {
        AuthorizationStatus = status;
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment
{
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? AuthorizeRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }
    public string? VoidRequestId { get; private set; }
    public string? Currency { get; private set; }

    public string EnsureAuthorizeRequestId(int orderId)
    {
        AuthorizeRequestId ??= $"eshop-auth-{orderId}";
        return AuthorizeRequestId;
    }

    public string EnsureCaptureRequestId(int orderId)
    {
        CaptureRequestId ??= $"eshop-capture-{orderId}";
        return CaptureRequestId;
    }

    public string EnsureVoidRequestId(int orderId)
    {
        VoidRequestId ??= $"eshop-void-{orderId}";
        return VoidRequestId;
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string? payPalOrderStatus,
        string authorizationId,
        string? authorizationStatus,
        DateTimeOffset? expiration,
        decimal authorizedAmount,
        string currency)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
    }

    public void RecordReauthorization(
        string authorizationId,
        string? authorizationStatus,
        DateTimeOffset? expiration)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
    }

    public void RecordCapture(
        string captureId,
        string? captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid(string? authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus ?? "VOIDED";
    }
}

using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment
{
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? Currency { get; private set; }
    public string? AuthorizeRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }
    public string? VoidRequestId { get; private set; }

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string? authorizationStatus,
        DateTimeOffset? expiration,
        string currency)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
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

    public void NoteCaptureId(string captureId)
    {
        CaptureId = captureId;
    }

    public string EnsureAuthorizeRequestId()
    {
        if (string.IsNullOrEmpty(AuthorizeRequestId))
        {
            AuthorizeRequestId = $"eshop-auth-{Guid.NewGuid():N}";
        }

        return AuthorizeRequestId;
    }

    public string EnsureCaptureRequestId()
    {
        if (string.IsNullOrEmpty(CaptureRequestId))
        {
            CaptureRequestId = $"eshop-cap-{Guid.NewGuid():N}";
        }

        return CaptureRequestId;
    }

    public string EnsureVoidRequestId()
    {
        if (string.IsNullOrEmpty(VoidRequestId))
        {
            VoidRequestId = $"eshop-void-{Guid.NewGuid():N}";
        }

        return VoidRequestId;
    }

    public string EnsureReauthorizeRequestId() => $"eshop-reauth-{Guid.NewGuid():N}";

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
    }

    public bool AuthorizationLooksStale(DateTimeOffset utcNow)
    {
        if (AuthorizationExpiration.HasValue && AuthorizationExpiration.Value <= utcNow)
        {
            return true;
        }

        return false;
    }
}

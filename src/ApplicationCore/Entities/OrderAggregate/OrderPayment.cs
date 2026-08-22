using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment
{
#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(
        string payPalOrderId,
        string payPalOrderStatus,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationCreateTime,
        DateTimeOffset? authorizationExpirationTime,
        string currency,
        decimal authorizedAmount)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationCreateTime = authorizationCreateTime;
        AuthorizationExpirationTime = authorizationExpirationTime;
        Currency = currency;
        AuthorizedAmount = authorizedAmount;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public string PayPalOrderId { get; private set; }
    public string PayPalOrderStatus { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreateTime { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string Currency { get; private set; }
    public decimal AuthorizedAmount { get; private set; }

    public void UpdateAuthorization(string authorizationId, string status, DateTimeOffset? createTime, DateTimeOffset? expirationTime)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        if (createTime.HasValue)
        {
            AuthorizationCreateTime = createTime;
        }
        AuthorizationExpirationTime = expirationTime;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
        VoidedAt = DateTimeOffset.UtcNow;
    }
}

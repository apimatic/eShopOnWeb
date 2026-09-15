using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment
{
    public string? Currency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public string? AuthorizeRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }
    public string? VoidRequestId { get; private set; }
    public int ReauthorizeCount { get; private set; }

    public decimal RemainingRefundable =>
        Math.Max(0, (CapturedAmount ?? 0m) - RefundedAmount);

    public bool HasHold => !string.IsNullOrEmpty(AuthorizationId);
    public bool HasCapture => !string.IsNullOrEmpty(CaptureId);

    internal void RecordPayPalOrder(string orderId, string? status, string currency, string authorizeRequestId)
    {
        PayPalOrderId = orderId;
        PayPalOrderStatus = status;
        Currency = currency;
        AuthorizeRequestId = authorizeRequestId;
    }

    internal void RecordAuthorization(string authorizationId, string? status, DateTimeOffset? expiration, string? paypalOrderStatus)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
        if (paypalOrderStatus != null)
            PayPalOrderStatus = paypalOrderStatus;
    }

    internal void RecordReauthorization(string authorizationId, string? status, DateTimeOffset? expiration)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
        ReauthorizeCount++;
    }

    internal void RecordCapture(string captureId, string? status, decimal capturedAmount, decimal? paypalFee, decimal? netAmount, string captureRequestId, string? authorizationStatus)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        CaptureRequestId = captureRequestId;
        if (authorizationStatus != null)
            AuthorizationStatus = authorizationStatus;
    }

    internal void RecordVoid(string? authorizationStatus, string voidRequestId)
    {
        AuthorizationStatus = authorizationStatus;
        VoidRequestId = voidRequestId;
    }

    internal void AddRefundedAmount(decimal amount, string? captureStatus)
    {
        RefundedAmount += amount;
        if (captureStatus != null)
            CaptureStatus = captureStatus;
    }
}

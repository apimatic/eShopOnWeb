using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Tracks the PayPal-owned state for an order's payment: the ids and current status of the
/// authorization (hold), and, once fulfilled, the capture and what PayPal reported it took.
/// This is an EF Core owned type of <see cref="Order"/> — it has no identity of its own.
/// </summary>
public class OrderPayment
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(string payPalOrderId, string authorizationId, string authorizationStatus,
        decimal authorizedAmount, string currency, DateTimeOffset? authorizationExpiresAt)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        AuthorizationExpiresAt = authorizationExpiresAt;
    }

    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }

    public decimal RefundedAmount { get; private set; }

    internal void UpdateAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? authorizationExpiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
    }

    internal void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFeeAmount, decimal netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFeeAmount = payPalFeeAmount;
        NetAmount = netAmount;
    }

    internal void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
    }

    internal void RecordRefund(decimal amount, string status)
    {
        RefundedAmount += amount;
        CaptureStatus = status;
    }
}

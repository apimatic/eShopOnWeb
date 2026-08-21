using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// PayPal-owned identifiers and status for an order payment. No card PAN/CVC is stored.
/// </summary>
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

    public string? Currency { get; private set; }
    public string? InvoiceId { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }

    public void RecordPayPalOrder(string payPalOrderId, string? status, string currency, string? invoiceId = null)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = status;
        Currency = currency;
        if (!string.IsNullOrEmpty(invoiceId))
        {
            InvoiceId = invoiceId;
        }
    }

    public void AssignInvoiceId(string invoiceId)
    {
        InvoiceId = invoiceId;
    }

    public void RecordAuthorization(
        string authorizationId,
        string? status,
        DateTimeOffset? expiration,
        decimal authorizedAmount,
        string currency)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
    }

    public void RecordCapture(
        string captureId,
        string? status,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        string currency)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        Currency = currency;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid(string? authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus ?? "VOIDED";
    }

    public void UseSavedPaymentMethod(int savedPaymentMethodId)
    {
        SavedPaymentMethodId = savedPaymentMethodId;
    }
}

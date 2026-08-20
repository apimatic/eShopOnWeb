using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// PayPal-owned identifiers and amounts attached to an eShop order so later
/// capture, void, reauthorize, refund, and reconciliation can act on the same payment.
/// </summary>
public class OrderPayment
{
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public string? Currency { get; private set; }
    public string? VaultId { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }
    public string MerchantInvoiceId { get; private set; } = string.Empty;

    public void AssignInvoiceId(string invoiceId)
    {
        MerchantInvoiceId = invoiceId;
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string payPalOrderStatus,
        string authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        string currency,
        DateTimeOffset? expiresAt,
        string? vaultId,
        int? savedPaymentMethodId)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
        VaultId = vaultId;
        SavedPaymentMethodId = savedPaymentMethodId;
    }

    public void UpdateAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netProceeds,
        string? authorizationStatus = null)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetProceeds = netProceeds;
        CapturedAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrEmpty(authorizationStatus))
        {
            AuthorizationStatus = authorizationStatus;
        }
        else
        {
            AuthorizationStatus = "CAPTURED";
        }
    }

    public void RecordVoid(string? authorizationStatus)
    {
        AuthorizationStatus = string.IsNullOrEmpty(authorizationStatus) ? "VOIDED" : authorizationStatus;
    }
}

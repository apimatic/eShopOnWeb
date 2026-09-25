using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-owned state of an order's payment: the ids and current statuses for the hold
/// (authorization) and the capture, plus the settlement figures PayPal reports at capture.
/// Owned by <see cref="Order"/>. Carries no card details — those never touch this database.
/// </summary>
public class OrderPayment
{
    // Required by EF Core
    private OrderPayment() { }

    public OrderPayment(string currency, decimal amount, string payPalOrderId,
        int? savedPaymentMethodId)
    {
        Currency = currency;
        Amount = amount;
        PayPalOrderId = payPalOrderId;
        SavedPaymentMethodId = savedPaymentMethodId;
    }

    /// <summary>ISO-4217 currency the order is charged in (from configuration).</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>The order total that was authorized, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's checkout order id (from CreateOrder).</summary>
    public string PayPalOrderId { get; private set; } = string.Empty;

    /// <summary>PayPal's authorization id — the hold. Used to capture, void or re-authorize.</summary>
    public string? AuthorizationId { get; private set; }

    /// <summary>PayPal's current authorization status (e.g. CREATED, VOIDED).</summary>
    public string? AuthorizationStatus { get; private set; }

    /// <summary>When the current authorization goes stale; capture must be renewed after this.</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>PayPal's capture id — set at fulfilment. Used to refund.</summary>
    public string? CaptureId { get; private set; }

    /// <summary>PayPal's current capture status (e.g. COMPLETED, REFUNDED).</summary>
    public string? CaptureStatus { get; private set; }

    /// <summary>Gross amount PayPal captured at fulfilment.</summary>
    public decimal? CapturedGross { get; private set; }

    /// <summary>PayPal's fee on the capture.</summary>
    public decimal? PaypalFee { get; private set; }

    /// <summary>Net proceeds to the merchant after PayPal's fee.</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>The saved card used to pay, if any (Flow 2). Null for a one-off card payment.</summary>
    public int? SavedPaymentMethodId { get; private set; }

    internal void RecordAuthorization(string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    internal void RenewAuthorization(string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt)
        => RecordAuthorization(authorizationId, authorizationStatus, expiresAt);

    internal void RecordCapture(string captureId, string captureStatus, decimal gross,
        decimal? fee, decimal? net)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGross = gross;
        PaypalFee = fee;
        NetAmount = net;
    }

    internal void SetAuthorizationStatus(string status) => AuthorizationStatus = status;

    internal void SetCaptureStatus(string status) => CaptureStatus = status;
}

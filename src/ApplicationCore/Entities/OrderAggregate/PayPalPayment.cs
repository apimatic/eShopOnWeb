namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The slice of payment state that PayPal owns for an order: the ids and current statuses
/// of the hold (authorization) and the capture, plus the money PayPal reported at capture
/// (gross captured, PayPal fee, net proceeds to the merchant).
///
/// Modelled as an owned value object of <see cref="Order"/> (mapped inline into the Orders
/// table, like <see cref="Address"/>). It carries no card data — full card details are never
/// stored by this application.
/// </summary>
public class PayPalPayment
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PayPalPayment() { }

    public PayPalPayment(string payPalOrderId, string authorizationId, string authorizationStatus, string currency, string reference)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        Currency = currency;
        Reference = reference;
    }

    /// <summary>The PayPal checkout order id (v2 Orders API).</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>Stable, unique reference we stamp on the PayPal order (invoice_id) so the
    /// reconciliation report can line PayPal's transactions up against eShop orders.</summary>
    public string Reference { get; private set; }

    /// <summary>The PayPal authorization id (the hold). Renewed if the hold goes stale.</summary>
    public string AuthorizationId { get; private set; }

    public string AuthorizationStatus { get; private set; }

    /// <summary>The PayPal capture id, populated at fulfilment.</summary>
    public string? CaptureId { get; private set; }

    public string? CaptureStatus { get; private set; }

    public decimal? CapturedAmount { get; private set; }

    public decimal? PayPalFee { get; private set; }

    public decimal? NetAmount { get; private set; }

    public string Currency { get; private set; }

    public void RenewAuthorization(string authorizationId, string authorizationStatus)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
    }

    public void SetAuthorizationStatus(string authorizationStatus) => AuthorizationStatus = authorizationStatus;

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
    }
}

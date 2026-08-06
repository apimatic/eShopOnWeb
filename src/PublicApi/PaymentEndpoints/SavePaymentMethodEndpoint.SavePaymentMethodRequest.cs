namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Card to save for the signed-in shopper. Sent to PayPal's vault; the raw details are never
/// stored or logged by this application. Expiry may be "YYYY-MM", "MM/YY" or "MM/YYYY".
/// </summary>
public class SavePaymentMethodRequest : BaseRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public CardBillingAddressDto? BillingAddress { get; set; }
}

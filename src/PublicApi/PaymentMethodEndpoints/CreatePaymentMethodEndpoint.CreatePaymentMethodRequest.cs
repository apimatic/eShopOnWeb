namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public string CardNumber { get; set; } = default!;

    /// <summary>"YYYY-MM"</summary>
    public string CardExpiry { get; set; } = default!;

    public string CardSecurityCode { get; set; } = default!;
    public string? CardholderName { get; set; }

    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressCity { get; set; }
    public string? BillingAddressState { get; set; }
    public string? BillingAddressPostalCode { get; set; }

    /// <summary>2-letter ISO 3166-1 country code, e.g. "US".</summary>
    public string BillingAddressCountryCode { get; set; } = default!;
}

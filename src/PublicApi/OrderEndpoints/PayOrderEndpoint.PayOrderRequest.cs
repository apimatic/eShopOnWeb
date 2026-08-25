namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Set this to pay with a previously saved card (see POST /api/payment-methods) instead of raw card details below.</summary>
    public int? PaymentMethodId { get; set; }

    public string? CardNumber { get; set; }

    /// <summary>"YYYY-MM"</summary>
    public string? CardExpiry { get; set; }

    public string? CardSecurityCode { get; set; }
    public string? CardholderName { get; set; }

    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressCity { get; set; }
    public string? BillingAddressState { get; set; }
    public string? BillingAddressPostalCode { get; set; }

    /// <summary>2-letter ISO 3166-1 country code, e.g. "US". Required when paying with raw card details.</summary>
    public string? BillingAddressCountryCode { get; set; }
}

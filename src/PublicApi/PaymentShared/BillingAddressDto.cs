namespace Microsoft.eShopWeb.PublicApi.PaymentShared;

/// <summary>Billing address of a card on an incoming request. Only <see cref="CountryCode"/> is required.</summary>
public class BillingAddressDto
{
    /// <summary>Two-letter ISO-3166-1 country code (required by PayPal), e.g. "US".</summary>
    public string? CountryCode { get; set; }

    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }

    /// <summary>State / province.</summary>
    public string? AdminArea1 { get; set; }

    /// <summary>City / town.</summary>
    public string? AdminArea2 { get; set; }

    public string? PostalCode { get; set; }
}

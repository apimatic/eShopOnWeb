namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>A card billing address. <see cref="CountryCode"/> is a two-letter ISO country code (e.g. "US").</summary>
public class BillingAddressInput
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "US";
}

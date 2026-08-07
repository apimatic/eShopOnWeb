namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// The billing address of a card. Maps to PayPal's card <c>billing_address</c>; only
/// <see cref="CountryCode"/> is required by the PayPal contract.
/// </summary>
public sealed class BillingAddress
{
    public BillingAddress(
        string countryCode,
        string? addressLine1 = null,
        string? addressLine2 = null,
        string? adminArea1 = null,
        string? adminArea2 = null,
        string? postalCode = null)
    {
        CountryCode = countryCode;
        AddressLine1 = addressLine1;
        AddressLine2 = addressLine2;
        AdminArea1 = adminArea1;
        AdminArea2 = adminArea2;
        PostalCode = postalCode;
    }

    /// <summary>Two-letter ISO-3166-1 country code (required by PayPal).</summary>
    public string CountryCode { get; }

    public string? AddressLine1 { get; }
    public string? AddressLine2 { get; }

    /// <summary>State / province (PayPal <c>admin_area_1</c>).</summary>
    public string? AdminArea1 { get; }

    /// <summary>City / town (PayPal <c>admin_area_2</c>).</summary>
    public string? AdminArea2 { get; }

    public string? PostalCode { get; }
}

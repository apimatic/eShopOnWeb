namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Billing address for a card. Mirrors PayPal's address shape; only <see cref="CountryCode"/> is required
/// by PayPal for card processing.
/// </summary>
public class BillingAddressDetails
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>Two-letter country code, e.g. US. Required by PayPal for card processing.</summary>
    public string CountryCode { get; set; } = "US";
}

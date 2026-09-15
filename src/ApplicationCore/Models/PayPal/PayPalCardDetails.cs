namespace Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

/// <summary>
/// Raw card details for a one-off card payment or for vaulting. These flow straight through to
/// PayPal and are never persisted in the application database nor written to logs.
/// </summary>
public class PayPalCardDetails
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in PayPal's required YYYY-MM form.</summary>
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }
    public string? Name { get; set; }

    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }

    /// <summary>City (PayPal admin_area_2).</summary>
    public string? BillingCity { get; set; }

    /// <summary>State/province (PayPal admin_area_1).</summary>
    public string? BillingState { get; set; }

    public string? BillingPostalCode { get; set; }

    /// <summary>Two-letter country code (PayPal country_code).</summary>
    public string? BillingCountryCode { get; set; }
}

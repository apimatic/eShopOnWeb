namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>
/// Raw card details supplied for a one-off payment or to vault a card. This is a transient
/// input only: it is passed to PayPal and never persisted in the application's database and
/// never written to logs.
/// </summary>
public class CardDetails
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in PayPal's "YYYY-MM" form.</summary>
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }
    public string? Name { get; set; }

    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }

    /// <summary>State / province (PayPal admin_area_1).</summary>
    public string? BillingAdminArea1 { get; set; }

    /// <summary>City (PayPal admin_area_2).</summary>
    public string? BillingAdminArea2 { get; set; }

    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }
}

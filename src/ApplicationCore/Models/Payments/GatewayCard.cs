namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// Full card details in transit to the payment provider. Instances of this type must
/// never be persisted or logged.
/// </summary>
public class GatewayCard
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM format.</summary>
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public GatewayCardAddress? BillingAddress { get; set; }
}

public class GatewayCardAddress
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}

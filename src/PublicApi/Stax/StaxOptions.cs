namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>Bound from the "Stax:" configuration section (values come from user-secrets / environment).</summary>
public class StaxOptions
{
    public const string SectionName = "Stax";
    public const string DefaultBaseUrl = "https://apiprod.fattlabs.com";

    public string? ApiKey { get; set; }
    public string? PlatformApiKey { get; set; }
    public string? BaseUrl { get; set; }

    /// <summary>Hosted invoice page Stax links to from scheduled invoices.</summary>
    public string InvoiceUrl { get; set; } = "https://app.staxpayments.com/#/bill/";
}

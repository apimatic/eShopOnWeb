namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Bare product-family handle. The wire value is this handle prefixed with "handle:".</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }
}

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section; secrets are supplied via
/// environment variables / .NET user-secrets, never from files in the repo.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (used as the Basic-auth username; password is literally "x" per the OpenAPI spec).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The subdomain of the Maxio Advanced Billing site (the {site} server variable in the OpenAPI spec).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>API handle of the Product Family that contains the subscription plans offered in the shop.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim;
    /// otherwise the base address is derived from <see cref="Subdomain"/> using the
    /// spec's default (US) server template https://{site}.chargify.com.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string ResolveBaseAddress()
        => !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl.TrimEnd('/')
            : $"https://{Subdomain}.chargify.com";
}

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Binds to the "Maxio" configuration section. Values are supplied via
/// .NET user-secrets/environment variables and must never be hard-coded,
/// since the same build has to run against different Maxio sites/catalogs.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSectionName = "Maxio";

    /// <summary>API key used as the HTTP Basic auth username (password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Advanced Billing site subdomain, e.g. "cp-exp-3" for https://cp-exp-3.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When unset, it is derived from
    /// <see cref="Subdomain"/> using Maxio's default (US) production server template.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl() =>
        string.IsNullOrWhiteSpace(BaseUrl) ? $"https://{Subdomain}.chargify.com" : BaseUrl;
}

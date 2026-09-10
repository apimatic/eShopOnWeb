namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio settings, bound from the <c>Maxio</c> configuration section. Values come
/// from user-secrets / environment — never from a file committed to the repo. See
/// <see cref="MaxioServiceCollectionExtensions"/> for the startup validation that refuses to boot
/// when a required value is missing.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio (Chargify) API key — the Basic-auth username; the password is the literal "x".</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, substituted into the default <c>https://{site}.chargify.com</c> base URL.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are exposed as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/> — e.g. to point at a mock or a differently-hosted site.
    /// </summary>
    public string? BaseUrl { get; set; }
}

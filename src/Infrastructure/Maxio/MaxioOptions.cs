namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Binds to the "Maxio" configuration section. Values are supplied via .NET user-secrets /
/// environment variables in every environment - never hard-code them, since the same build
/// must run against a different Maxio site and catalog.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (Basic Auth username; password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, used to derive the API base URL when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>API handle of the product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional override for the API base address. When set, used verbatim instead of deriving one from <see cref="Subdomain"/>.</summary>
    public string? BaseUrl { get; set; }
}

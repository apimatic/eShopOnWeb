using System;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings bound from the "Maxio:" configuration section.
/// Secrets come from .NET user-secrets (development) or environment variables
/// (Maxio__ApiKey, ...), never from a file in the repository.
/// </summary>
public class MaxioSettings
{
    public const string SECTION_NAME = "Maxio";

    /// <summary>Maxio API key (the Basic-auth user name; the password is always "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain.</summary>
    public string? Subdomain { get; set; }

    /// <summary>"US" or "EU". Blank means US.</summary>
    public string? Environment { get; set; }

    /// <summary>Optional override. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    public ServerEnvironment ResolveEnvironment()
    {
        if (string.IsNullOrWhiteSpace(Environment) || Environment.Trim().Equals("US", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Us;
        }

        if (Environment.Trim().Equals("EU", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Eu;
        }

        throw new InvalidOperationException($"{SECTION_NAME}:Environment must be 'US' or 'EU'.");
    }

    /// <summary>Refuses to start the host when the settings cannot work.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException($"{SECTION_NAME}:ApiKey is not configured (set it with user-secrets or the {SECTION_NAME}__ApiKey environment variable).");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException($"{SECTION_NAME}:Subdomain is not configured (set it with user-secrets or the {SECTION_NAME}__Subdomain environment variable).");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl)
            && !(Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
        {
            throw new InvalidOperationException($"{SECTION_NAME}:BaseUrl must be an absolute http(s) URL.");
        }

        ResolveEnvironment();
    }
}

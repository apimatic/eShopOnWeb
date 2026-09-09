using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Values are bound from the "Maxio" configuration section (e.g. from user-secrets or
/// environment variables) and are never hard-coded:
///   Maxio:ApiKey                 - Maxio Advanced Billing API key (basic-auth username)
///   Maxio:Subdomain              - Maxio site subdomain (e.g. "cp-exp-1")
///   Maxio:ProductFamilyHandle    - handle of the product family holding the plans
///   Maxio:BaseUrl                - optional. When set, used verbatim as the API base address
///                                  instead of deriving one from the subdomain.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: BaseUrl verbatim when configured, otherwise
    /// derived from the site subdomain.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/') + "/");
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set either Maxio:BaseUrl or Maxio:Subdomain.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/");
    }
}

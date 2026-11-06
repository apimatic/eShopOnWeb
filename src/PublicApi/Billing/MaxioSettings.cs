using System;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings bound from the "Maxio" configuration section
/// (Maxio:ApiKey, Maxio:Subdomain, Maxio:Environment, Maxio:BaseUrl).
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional override; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingException(503, "Billing is not configured (Maxio:Subdomain or Maxio:BaseUrl is missing).");
        }

        var host = (Environment ?? "US").Trim().ToUpperInvariant() switch
        {
            "US" => "https://" + Subdomain + ".chargify.com",
            "EU" => "https://" + Subdomain + ".ebilling.maxio.com",
            _ => throw new BillingException(503, "Billing is not configured (unknown Maxio:Environment).")
        };
        return new Uri(host, UriKind.Absolute);
    }
}

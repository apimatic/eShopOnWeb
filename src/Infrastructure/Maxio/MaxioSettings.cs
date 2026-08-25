using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values are supplied via user-secrets or environment variables; none are committed to the repo.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (used as the Basic-auth username; password is "x" per the spec).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, used to template the server URL https://{site}.chargify.com from the OpenAPI spec.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>API handle of the product family containing the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional override for the API base address. When set, used verbatim instead of deriving from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    public Uri GetBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: set either '{SectionName}:BaseUrl' or '{SectionName}:Subdomain'.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException($"Maxio is not configured: '{SectionName}:ApiKey' is required.");
        }

        _ = GetBaseAddress();
    }
}

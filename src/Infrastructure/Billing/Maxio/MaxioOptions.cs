using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. Secret values must come from
/// environment variables or user-secrets — never from source.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, used verbatim as the Advanced Billing API base address
    /// instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional. When <see cref="BaseUrl"/> is empty, <c>EU</c> selects the EU host
    /// from the OpenAPI server catalog; any other value uses the US host.
    /// </summary>
    public string? Environment { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    public void EnsureConfigured()
    {
        if (IsConfigured)
        {
            return;
        }

        throw new BillingConfigurationException(
            "Maxio billing is not configured. Set Maxio:ApiKey, Maxio:ProductFamilyHandle, and either Maxio:BaseUrl or Maxio:Subdomain.");
    }

    /// <summary>
    /// Resolves the Advanced Billing API root from the OpenAPI server catalog:
    /// US <c>https://{site}.chargify.com</c>, EU <c>https://{site}.ebilling.maxio.com</c>,
    /// or a verbatim <see cref="BaseUrl"/> override.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return NormalizeBaseUrl(BaseUrl);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException("Set Maxio:BaseUrl or Maxio:Subdomain to locate the Advanced Billing API.");
        }

        var host = string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase)
            ? $"{Subdomain.Trim()}.ebilling.maxio.com"
            : $"{Subdomain.Trim()}.chargify.com";

        return $"https://{host}/";
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        if (!trimmed.EndsWith('/'))
        {
            trimmed += "/";
        }

        return trimmed;
    }
}

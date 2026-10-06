using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the
/// "Maxio" configuration section. Values are supplied via user-secrets or
/// environment variables - never hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>API key used as the Basic-auth username ("X" is the password).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain; the API base becomes https://{Subdomain}.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family holding the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional full API base override (e.g., a non-US datacenter or a mock).
    /// When set it is used verbatim instead of deriving from Subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: BaseUrl verbatim when configured,
    /// otherwise https://{Subdomain}.chargify.com per the Billing API docs.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio configuration is incomplete: set {SectionName}:BaseUrl or {SectionName}:Subdomain.");
        }

        return $"https://{Subdomain.TrimEnd('/')}.chargify.com";
    }
}
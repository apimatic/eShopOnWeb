using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing settings, bound from the "Maxio" configuration
/// section. Values are supplied via .NET user-secrets / environment configuration and are
/// never committed to the repository.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (used as the HTTP Basic auth username; password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, e.g. "acme" for https://acme.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim; otherwise the base URL is
    /// derived from <see cref="Subdomain"/> as https://{Subdomain}.chargify.com.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the effective API base address (honouring an explicit override).</summary>
    public string ResolveBaseUrl() =>
        string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl!.TrimEnd('/');

    /// <summary>Validates that the minimum required settings are present; throws otherwise.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException(
                $"Maxio configuration is missing '{SectionName}:{nameof(ApiKey)}'. " +
                "Set it via user-secrets (e.g. dotnet user-secrets set \"Maxio:ApiKey\" <value>).");

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException(
                $"Maxio configuration requires either '{SectionName}:{nameof(Subdomain)}' or " +
                $"'{SectionName}:{nameof(BaseUrl)}'.");

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException(
                $"Maxio configuration is missing '{SectionName}:{nameof(ProductFamilyHandle)}'.");
    }
}

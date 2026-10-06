using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section (user-secrets / environment variables):
/// Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle and the optional Maxio:BaseUrl override.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maxio site subdomain (e.g. "acme" for https://acme.chargify.com).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// API handle of the product family that contains the subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address
    /// instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional handle of the plan subscribed to when none is specified.
    /// When unset, the first unarchived plan of the product family is used.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: either Maxio:BaseUrl or Maxio:Subdomain must be set.");
        }

        return $"https://{Subdomain.TrimEnd('/')}.chargify.com";
    }

    public void Validate()
    {
        var (isValid, error) = ValidateQuiet();
        if (!isValid)
        {
            throw new InvalidOperationException(error);
        }
    }

    /// <summary>
    /// Non-throwing configuration check; returns (false, reason) when incomplete.
    /// </summary>
    public (bool IsValid, string? Error) ValidateQuiet()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            return (false, "Maxio:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            return (false, "Maxio:ProductFamilyHandle is required.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            return (false, "Either Maxio:BaseUrl or Maxio:Subdomain is required.");
        }

        // Forces resolution so a malformed site configuration is caught here.
        ResolveBaseUrl();
        return (true, null);
    }
}
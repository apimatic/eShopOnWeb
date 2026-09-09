using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration. Bound from the
/// <c>Maxio</c> configuration section; values never live in the repository —
/// they arrive via user-secrets / environment variables.
/// </summary>
public sealed class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address. When set it replaces the URL derived
    /// from <see cref="Subdomain"/>; when null the subdomain is used.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Throws a descriptive <see cref="InvalidOperationException"/> when a
    /// required value is missing. Called lazily (first use of the billing
    /// service) so hosts that never touch billing — e.g. tests — keep working.
    /// </summary>
    public void Validate()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add($"{CONFIG_NAME}:{nameof(ApiKey)}");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) missing.Add($"{CONFIG_NAME}:{nameof(ProductFamilyHandle)}");
        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            missing.Add($"{CONFIG_NAME}:{nameof(Subdomain)} (or {CONFIG_NAME}:{nameof(BaseUrl)})");
        }
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Maxio billing is not configured. Set the following configuration keys " +
                "(e.g. via user-secrets or environment variables): " + string.Join(", ", missing));
        }
    }
}

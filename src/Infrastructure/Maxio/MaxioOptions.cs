using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Connection settings for Maxio Advanced Billing, bound from the "Maxio" configuration
/// section. Values are supplied per environment (user secrets in development); nothing here
/// may be hard-coded.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, used verbatim as the API base address instead of deriving
    /// one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public void Validate()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add($"{SectionName}:{nameof(ApiKey)}");
        if (string.IsNullOrWhiteSpace(Subdomain)) missing.Add($"{SectionName}:{nameof(Subdomain)}");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) missing.Add($"{SectionName}:{nameof(ProductFamilyHandle)}");
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Maxio Advanced Billing is not configured. Missing required settings: " +
                string.Join(", ", missing) + ".");
        }
    }
}

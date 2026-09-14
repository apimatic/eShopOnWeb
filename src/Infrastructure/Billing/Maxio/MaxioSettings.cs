using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings bound from the <c>Maxio</c> configuration section.
/// </summary>
/// <remarks>
/// No value here has a hard-coded default: the same build must run against a different Maxio site and a
/// different catalog. Supply them through any <c>IConfiguration</c> source — in development they are loaded
/// from .NET user-secrets, so no credential ever enters the repository.
/// </remarks>
public sealed class MaxioSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the site API key. Sent as the basic-auth user name.</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the Maxio site subdomain, substituted into the default base URL.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — handle of the product family whose products are sellable plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// <c>Maxio:BaseUrl</c> — optional. When set, it is used verbatim as the API base address instead of a
    /// URL derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Returns the reasons this configuration cannot be used, or an empty list when it is usable.
    /// </summary>
    /// <remarks>
    /// <see cref="Subdomain"/> is only required when <see cref="BaseUrl"/> is absent, because an explicit
    /// base URL replaces the subdomain-derived one entirely. Validating the subdomain matters: the SDK's
    /// default base URL is a <c>{site}</c> template, so an unset subdomain does not fail at construction —
    /// every request would silently go to a literal <c>subdomain</c> host instead.
    /// </remarks>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            errors.Add($"'{SectionName}:{nameof(ApiKey)}' is not configured.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            errors.Add($"'{SectionName}:{nameof(Subdomain)}' is not configured (required unless '{SectionName}:{nameof(BaseUrl)}' is set).");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            !Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out _))
        {
            errors.Add($"'{SectionName}:{nameof(BaseUrl)}' is not an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            errors.Add($"'{SectionName}:{nameof(ProductFamilyHandle)}' is not configured.");
        }

        return errors;
    }
}

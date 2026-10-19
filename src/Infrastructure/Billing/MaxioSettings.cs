using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Values come from user-secrets (development) or the
/// host's configuration (deployments) — never from a file in the repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the Basic-auth username. Secret.</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the site subdomain the API base address is derived from.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set it is used verbatim instead of deriving one from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> — optional, <c>US</c> (default) or <c>EU</c>: the region the site is hosted in.</summary>
    public string? Environment { get; set; }

    /// <summary><c>Maxio:DefaultPlanHandle</c> — optional; the plan used when a subscribe request names none.</summary>
    public string? DefaultPlanHandle { get; set; }

    public bool IsEuRegion => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Fails host start-up when a required Maxio setting is missing, rather than letting the first call
/// go out unauthenticated. Messages name the configuration key, never its value.
/// </summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings settings)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured. Set it in user-secrets or the host configuration.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is not configured.");
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                failures.Add("Maxio:BaseUrl must be an absolute http(s) URL when it is set.");
            }
        }
        else if (string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            failures.Add("Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set).");
        }

        if (!string.IsNullOrWhiteSpace(settings.Environment) &&
            !string.Equals(settings.Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase) &&
            !settings.IsEuRegion)
        {
            failures.Add("Maxio:Environment must be 'US' or 'EU' when it is set.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

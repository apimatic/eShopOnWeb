using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Values come from user-secrets / environment /
/// a secret store — never from appsettings files checked into the repository.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the Maxio API key (Basic-auth user name).</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the Maxio site subdomain; selects the site (sandbox vs live).</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// <c>Maxio:BaseUrl</c> — optional. When set, used verbatim as the API base address instead of the
    /// address derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// Refuses to start the host when a Maxio setting is missing, so a misconfiguration is found at deploy
/// time instead of as a 401 on the first shopper's request. Messages name keys, never values.
/// </summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings settings)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured. Set it via user-secrets, environment or your secret store.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                failures.Add("Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set).");
            }
        }
        else if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri)
                 || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Maxio:BaseUrl must be an absolute http(s) URL.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

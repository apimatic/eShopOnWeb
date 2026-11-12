using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings of the Maxio billing integration, bound from the <c>Maxio:</c> configuration section.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio:ApiKey — the site API key (the Basic-auth user name).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio:Subdomain — the Maxio site's subdomain.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Maxio:Environment — "US" (default) or "EU".</summary>
    public string? Environment { get; set; }

    /// <summary>Maxio:BaseUrl — optional override, used verbatim as the API's base address when set.</summary>
    public string? BaseUrl { get; set; }
}

public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured. Set it with user-secrets or the Maxio__ApiKey environment variable.");
        }
        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add("Maxio:Subdomain is not configured. Set it with user-secrets or the Maxio__Subdomain environment variable.");
        }
        if (!string.IsNullOrWhiteSpace(options.Environment)
            && !options.Environment.Trim().Equals("US", StringComparison.OrdinalIgnoreCase)
            && !options.Environment.Trim().Equals("EU", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Maxio:Environment must be 'US' or 'EU'.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

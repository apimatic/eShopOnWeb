using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>Bound from the "Maxio" configuration section.</summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (used as the Basic-auth username).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain, substituted into the Maxio host name.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that holds the subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address instead of deriving one from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Optional "US" (default) or "EU".</summary>
    public string? Environment { get; set; }
}

public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            errors.Add("Maxio:ApiKey is not configured. Set it via user-secrets or the Maxio__ApiKey environment variable.");
        if (string.IsNullOrWhiteSpace(options.Subdomain))
            errors.Add("Maxio:Subdomain is not configured.");
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            errors.Add("Maxio:ProductFamilyHandle is not configured.");
        if (!string.IsNullOrWhiteSpace(options.BaseUrl) && !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
            errors.Add("Maxio:BaseUrl is not an absolute URL.");
        if (!string.IsNullOrWhiteSpace(options.Environment) &&
            !options.Environment.Equals("US", StringComparison.OrdinalIgnoreCase) &&
            !options.Environment.Equals("EU", StringComparison.OrdinalIgnoreCase))
            errors.Add("Maxio:Environment must be US or EU.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

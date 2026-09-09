using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section (values are provided via
/// environment variables loaded into .NET user-secrets - never hard-coded).
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key (sent as the HTTP Basic username; password is "x").
    /// Sourced from the MAXIO_API_KEY environment variable.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maxio site subdomain (the site is selected by the subdomain in the API host).
    /// Sourced from the MAXIO_SITE_SUBDOMAIN environment variable.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the Maxio Product Family that contains the subscription plans.
    /// Sourced from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address override. When set, it is used instead
    /// of the base address derived from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The API base address to use: the BaseUrl override verbatim when provided,
    /// otherwise the standard Advanced Billing US host for the configured subdomain.
    /// </summary>
    public Uri ResolveApiBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/') + "/");
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set Maxio:BaseUrl or Maxio:Subdomain.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/");
    }
}

/// <summary>
/// Fail-fast validation of the Maxio configuration at application startup.
/// </summary>
public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        if (options is null)
        {
            return ValidateOptionsResult.Fail("Maxio configuration section is missing.");
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BaseUrl) && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add("Either 'Maxio:BaseUrl' or 'Maxio:Subdomain' must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("'Maxio:ApiKey' must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add("'Maxio:ProductFamilyHandle' must be configured.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

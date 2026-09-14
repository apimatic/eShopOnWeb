using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The Maxio hosting environments and their server templates, transcribed from the specification's
/// <c>info.x-server-configuration</c> block (maxio-spec/openapi.yaml):
/// <code>
/// environments:
///   - name: US   servers: [ { url: "https://{site}.chargify.com", name: production }, ... ]
///   - name: EU   servers: [ { url: "https://{site}.ebilling.maxio.com", name: production } ]
/// parameters:
///   - name: site   description: The subdomain for your Advanced Billing site.
/// </code>
/// Only the <c>production</c> server of each environment is used; the <c>ebb</c> server exists for
/// events-based-billing ingestion, which this integration does not perform.
/// </summary>
public static class MaxioEnvironments
{
    public const string UnitedStates = "US";
    public const string Europe = "EU";

    /// <summary>The specification's <c>default-environment</c>.</summary>
    public const string Default = UnitedStates;

    private static readonly IReadOnlyDictionary<string, string> ProductionServerTemplates =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [UnitedStates] = "https://{site}.chargify.com",
            [Europe] = "https://{site}.ebilling.maxio.com"
        };

    public static string SupportedList => string.Join(", ", ProductionServerTemplates.Keys.Select(k => $"'{k}'"));

    public static bool IsSupported(string? environment) =>
        !string.IsNullOrWhiteSpace(environment) && ProductionServerTemplates.ContainsKey(environment.Trim());

    /// <summary>
    /// Resolves the API base address: the <c>BaseUrl</c> override when set, otherwise the production
    /// server template of the configured environment with <c>{site}</c> replaced by the subdomain.
    /// </summary>
    /// <exception cref="SubscriptionBillingConfigurationException">The settings cannot produce a base address.</exception>
    public static Uri ResolveBaseAddress(MaxioSettings settings)
    {
        var problems = settings.Validate();
        if (problems.Count > 0)
        {
            throw new SubscriptionBillingConfigurationException(problems);
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return WithTrailingSlash(new Uri(settings.BaseUrl.Trim(), UriKind.Absolute));
        }

        var template = ProductionServerTemplates[settings.Environment.Trim()];
        var url = template.Replace("{site}", Uri.EscapeDataString(settings.Subdomain!.Trim()), StringComparison.Ordinal);
        return WithTrailingSlash(new Uri(url, UriKind.Absolute));
    }

    /// <summary>
    /// A base address has to end in "/" or <see cref="Uri"/> composition drops its last segment.
    /// </summary>
    private static Uri WithTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
}

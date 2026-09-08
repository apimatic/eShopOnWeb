using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Resolves the Maxio API base address from <see cref="MaxioOptions"/> following the
/// <c>x-server-configuration</c> block of the Maxio OpenAPI specification.
/// </summary>
internal static class MaxioBaseUrlResolver
{
    public const string UsHostTemplate = "https://{0}.chargify.com";
    public const string EuHostTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>
    /// Returns the base URL (no trailing slash) or <c>null</c> when it cannot be derived.
    /// </summary>
    public static string? TryResolve(MaxioOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain))
        {
            return null;
        }

        bool isEu = string.Equals(options.Environment, "EU", StringComparison.OrdinalIgnoreCase);
        string host = isEu
            ? string.Format(EuHostTemplate, options.Subdomain)
            : string.Format(UsHostTemplate, options.Subdomain);

        return host;
    }
}

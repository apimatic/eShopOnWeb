using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The Advanced Billing hosting environments and their server URL templates, taken verbatim
/// from the OpenAPI specification's server configuration (maxio-spec/openapi.yaml).
/// </summary>
public static class MaxioEnvironments
{
    public const string Us = "US";
    public const string Eu = "EU";

    private static readonly Dictionary<string, string> ServerUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        [Us] = "https://{site}.chargify.com",
        [Eu] = "https://{site}.ebilling.maxio.com"
    };

    public static IEnumerable<string> All => ServerUrls.Keys;

    public static bool IsKnown(string? environment) =>
        !string.IsNullOrWhiteSpace(environment) && ServerUrls.ContainsKey(environment!);

    public static string ServerUrlTemplate(string environment) =>
        ServerUrls.TryGetValue(environment, out var url)
            ? url
            : throw new ArgumentOutOfRangeException(nameof(environment), environment, "Unknown Maxio environment.");
}

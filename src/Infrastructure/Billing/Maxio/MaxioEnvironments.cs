using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// The Maxio Advanced Billing hosting environments and the API hosts they map to.
/// Per the Billing API server configuration: US sites live on <c>{site}.chargify.com</c>, EU sites
/// on <c>{site}.ebilling.maxio.com</c>.
/// </summary>
public static class MaxioEnvironments
{
    public const string Us = "US";
    public const string Eu = "EU";

    private const string UsHostSuffix = ".chargify.com";
    private const string EuHostSuffix = ".ebilling.maxio.com";

    public static bool IsSupported(string? environment) =>
        string.IsNullOrWhiteSpace(environment)
        || string.Equals(environment, Us, StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment, Eu, StringComparison.OrdinalIgnoreCase);

    public static string HostSuffixFor(string? environment) =>
        string.Equals(environment?.Trim(), Eu, StringComparison.OrdinalIgnoreCase) ? EuHostSuffix : UsHostSuffix;
}

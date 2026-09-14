using System.Collections.Generic;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Holds the configured Maxio client, or the reason there isn't one.
/// </summary>
/// <remarks>
/// Subscription billing is an additive capability: a deployment with no Maxio configuration must still serve
/// the catalog, basket and checkout. So a missing or invalid <c>Maxio</c> section does not stop the host from
/// starting - it makes the subscription endpoints answer "service unavailable" with an actionable message,
/// while everything else keeps working.
/// </remarks>
public sealed class MaxioClientAccessor
{
    private readonly MaxioAdvancedBillingClient? _client;
    private readonly string? _configurationError;

    public MaxioClientAccessor(MaxioAdvancedBillingClient client) => _client = client;

    public MaxioClientAccessor(IReadOnlyList<string> configurationProblems) =>
        _configurationError = string.Join(" ", configurationProblems);

    public bool IsConfigured => _client is not null;

    /// <summary>The configuration problems that prevented a client from being built, or an empty string.</summary>
    public string ConfigurationError => _configurationError ?? string.Empty;

    public MaxioAdvancedBillingClient Client =>
        _client ?? throw new BillingException(
            BillingFailureKind.NotConfigured,
            $"Subscription billing is not configured on this deployment. {_configurationError}".TrimEnd());
}

using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Hands out <see cref="MaxioSettings"/> that are known to be complete, turning a misconfiguration
/// into one actionable error instead of a null-reference somewhere downstream.
/// </summary>
/// <remarks>
/// Validation deliberately happens on use rather than at startup. Subscription billing is an
/// additive capability: a deployment that does not use it must still be able to serve the catalog,
/// basket and order endpoints, so an absent <c>Maxio</c> section degrades the billing routes to 503
/// instead of preventing the host from starting.
/// </remarks>
public class MaxioSettingsProvider
{
    private readonly IOptionsMonitor<MaxioSettings> _options;

    public MaxioSettingsProvider(IOptionsMonitor<MaxioSettings> options)
    {
        _options = options;
    }

    /// <summary>The current settings, whether or not they are usable.</summary>
    public MaxioSettings Current => _options.CurrentValue;

    /// <summary>
    /// The current settings, guaranteed complete.
    /// </summary>
    /// <exception cref="BillingConfigurationException">A mandatory setting has no value.</exception>
    public MaxioSettings GetValidated()
    {
        var settings = _options.CurrentValue;
        var missing = settings.DescribeMissingSettings();

        if (missing.Count > 0)
        {
            throw new BillingConfigurationException(
                "Subscription billing is not configured. Set " + string.Join(", ", missing) +
                " (for example with 'dotnet user-secrets set' or the matching " +
                string.Join(", ", MissingAsEnvironmentVariables(missing)) + " environment variables).");
        }

        try
        {
            settings.ResolveBaseAddress();
        }
        catch (UriFormatException ex)
        {
            throw new BillingConfigurationException(ex.Message, ex);
        }

        return settings;
    }

    private static string[] MissingAsEnvironmentVariables(System.Collections.Generic.IReadOnlyList<string> missing)
    {
        var names = new string[missing.Count];
        for (var i = 0; i < missing.Count; i++)
        {
            names[i] = missing[i].Replace(":", "__", StringComparison.Ordinal);
        }

        return names;
    }
}

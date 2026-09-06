using System;
using System.Collections.Generic;
using System.Linq;
using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maps a configured payment-collection-method string onto the provider's enum.
/// </summary>
/// <remarks>
/// Written out rather than parsed through a generic helper so an unrecognised configuration value fails
/// with a message naming the values that do work.
/// </remarks>
internal static class MaxioCollectionMethods
{
    private static readonly CollectionMethod[] Supported =
    {
        CollectionMethod.Automatic,
        CollectionMethod.Remittance,
        CollectionMethod.Prepaid,
        CollectionMethod.Invoice
    };

    public static IReadOnlyList<string> SupportedValues =>
        Supported.Select(method => method.Value).ToList();

    /// <summary>
    /// Resolves a configured value. A blank value is valid and means "not overridden".
    /// </summary>
    public static bool TryResolve(string? configuredValue, out CollectionMethod? method)
    {
        method = null;

        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return true;
        }

        var trimmed = configuredValue.Trim();

        foreach (var candidate in Supported)
        {
            if (string.Equals(candidate.Value, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                method = candidate;
                return true;
            }
        }

        return false;
    }
}

using System;
using System.Linq;
using AdvancedBilling.Standard.Models;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Maps the configured payment collection method onto the SDK's <see cref="CollectionMethod"/> enum.
/// </summary>
/// <remarks>
/// Advanced Billing accepts <c>remittance</c>, <c>automatic</c> and <c>prepaid</c> on sites using
/// Relationship Invoicing, and <c>invoice</c> or <c>automatic</c> on legacy Statements sites.
/// </remarks>
internal static class MaxioCollectionMethods
{
    private static readonly (string Name, CollectionMethod Value)[] Supported =
    {
        ("remittance", CollectionMethod.Remittance),
        ("automatic", CollectionMethod.Automatic),
        ("prepaid", CollectionMethod.Prepaid),
        ("invoice", CollectionMethod.Invoice),
    };

    public static string SupportedList => string.Join(", ", Supported.Select(m => m.Name));

    public static bool IsSupported(string? name) => TryParse(name, out _);

    public static bool TryParse(string? name, out CollectionMethod method)
    {
        var trimmed = name?.Trim();

        foreach (var (candidate, value) in Supported)
        {
            if (string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                method = value;
                return true;
            }
        }

        method = default;
        return false;
    }
}

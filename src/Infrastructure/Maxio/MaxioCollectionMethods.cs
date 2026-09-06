using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Values accepted by Maxio's <c>payment_collection_method</c> attribute.
/// </summary>
/// <remarks>
/// Mirrors the <c>CollectionMethod</c> enumeration of the Maxio Advanced Billing API. Sites on the
/// Relationship Invoicing architecture accept <c>remittance</c>, <c>automatic</c> and
/// <c>prepaid</c>; legacy Statements sites accept <c>invoice</c> and <c>automatic</c>.
/// </remarks>
public static class MaxioCollectionMethods
{
    public const string Automatic = "automatic";
    public const string Remittance = "remittance";
    public const string Prepaid = "prepaid";
    public const string Invoice = "invoice";

    private static readonly HashSet<string> Supported =
        new(StringComparer.OrdinalIgnoreCase) { Automatic, Remittance, Prepaid, Invoice };

    public static IReadOnlyCollection<string> All => Supported;

    public static bool IsSupported(string? value) => value is not null && Supported.Contains(value);
}

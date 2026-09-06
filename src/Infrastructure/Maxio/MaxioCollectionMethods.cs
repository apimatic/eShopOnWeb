using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The payment collection methods declared by the specification schema <c>Collection-Method</c>.
/// </summary>
public static class MaxioCollectionMethods
{
    public const string Automatic = "automatic";
    public const string Remittance = "remittance";
    public const string Prepaid = "prepaid";
    public const string Invoice = "invoice";

    private static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Automatic, Remittance, Prepaid, Invoice
    };

    public static string Supported => string.Join(", ", All.OrderBy(v => v, StringComparer.Ordinal));

    public static bool IsSupported(string? value) =>
        !string.IsNullOrWhiteSpace(value) && All.Contains(value.Trim());
}

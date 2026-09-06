using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The values of maxio-spec/components/schemas/Collection-Method.yaml.
/// <para>
/// "For legacy Statements Architecture valid options are - `invoice`, `automatic`. For current
/// Relationship Invoicing Architecture valid options are - `remittance`, `automatic`, `prepaid`."
/// Which subset a given site accepts depends on its architecture, so all four are accepted here and
/// the site has the final say.
/// </para>
/// </summary>
public static class CollectionMethods
{
    public const string Automatic = "automatic";
    public const string Remittance = "remittance";
    public const string Prepaid = "prepaid";
    public const string Invoice = "invoice";

    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        Automatic,
        Remittance,
        Prepaid,
        Invoice
    };

    public static string KnownList => string.Join(", ", Known.Order(StringComparer.Ordinal).Select(value => $"'{value}'"));

    public static bool IsKnown(string? value) => value is not null && Known.Contains(value);
}

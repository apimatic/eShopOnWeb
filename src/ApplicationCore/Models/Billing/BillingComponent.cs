using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A billable add-on defined on a product family, e.g. the metered "API Calls" component.
/// </summary>
public class BillingComponent
{
    /// <summary>
    /// The provider's identifier for a usage-metered component kind.
    /// </summary>
    public const string MeteredKind = "metered_component";

    public int Id { get; set; }
    public string? Handle { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The provider component kind, e.g. metered_component or quantity_based_component.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    public string? PricingScheme { get; set; }

    /// <summary>
    /// The price charged per unit in the site currency (e.g. 0.01), not in minor units.
    /// </summary>
    public decimal? UnitPrice { get; set; }

    public string? UnitName { get; set; }
    public int ProductFamilyId { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public bool Archived { get; set; }

    public bool IsMetered => string.Equals(Kind, MeteredKind, StringComparison.OrdinalIgnoreCase);
}

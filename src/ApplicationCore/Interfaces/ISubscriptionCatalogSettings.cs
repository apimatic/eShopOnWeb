namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic view of which catalog entities this deployment is configured against.
/// Only durable handles appear here: the provider assigns numeric ids and reassigns them whenever
/// the catalog is re-created, so handles are the identifiers the domain reasons about.
/// </summary>
public interface ISubscriptionCatalogSettings
{
    /// <summary>Handle of the product family holding the plans and the metered component.</summary>
    string ProductFamilyHandle { get; }

    /// <summary>Handle of the plan offered as the default subscribe target.</summary>
    string DefaultPlanHandle { get; }

    /// <summary>Handle of the second plan, used as the upgrade/downgrade target. May be empty.</summary>
    string AlternatePlanHandle { get; }

    /// <summary>Handle of the metered component usage is reported against.</summary>
    string MeteredComponentHandle { get; }
}

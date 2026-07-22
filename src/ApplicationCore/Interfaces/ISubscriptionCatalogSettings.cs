namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider catalogue identifiers the domain needs to know about, exposed as an abstraction so
/// ApplicationCore never depends on the provider's own options type.
/// </summary>
/// <remarks>
/// Only handles appear here: the provider reassigns numeric ids whenever the catalogue is
/// re-created, so handles are the durable identifiers and everything resolves from them.
/// </remarks>
public interface ISubscriptionCatalogSettings
{
    /// <summary>Handle of the product family holding the plans and the metered component.</summary>
    string ProductFamilyHandle { get; }

    /// <summary>Handle of the plan the storefront subscribes to by default.</summary>
    string DefaultProductHandle { get; }

    /// <summary>Handle of the plan offered as the upgrade/downgrade target.</summary>
    string AlternateProductHandle { get; }

    /// <summary>Handle of the metered component that pay-as-you-go usage accrues against.</summary>
    string MeteredComponentHandle { get; }
}

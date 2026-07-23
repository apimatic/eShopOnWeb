namespace Microsoft.eShopWeb;

/// <summary>
/// The provider-agnostic catalog configuration the subscription domain needs: which plans customers
/// may subscribe to and which component carries pay-as-you-go usage.
/// </summary>
/// <remarks>
/// Only durable <b>handles</b> live here. Numeric provider ids are deliberately absent because the
/// provider reassigns them whenever the catalog is re-created; every id is resolved from its handle
/// at call time instead.
/// </remarks>
public class SubscriptionSettings
{
    /// <summary>The configuration section both this and the provider settings bind from.</summary>
    public const string CONFIG_SECTION = "Maxio";

    /// <summary>Handle of the product family that holds the plans and the metered component.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Handle of the plan offered by default on the storefront.</summary>
    public string DefaultProductHandle { get; set; } = string.Empty;

    /// <summary>Handle of the alternate plan, the other end of the upgrade/downgrade pair.</summary>
    public string AlternateProductHandle { get; set; } = string.Empty;

    /// <summary>Handle of the metered component usage is recorded against.</summary>
    public string MeteredComponentHandle { get; set; } = string.Empty;

    /// <summary>
    /// Whether placing an eShopOnWeb order automatically records one unit of metered usage against
    /// the buyer's active subscription. Enabled by default; turning it off leaves admin-reported
    /// usage working unchanged.
    /// </summary>
    public bool RecordUsageOnOrderPlaced { get; set; } = true;
}

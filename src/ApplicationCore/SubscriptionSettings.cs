namespace Microsoft.eShopWeb;

/// <summary>
/// The non-secret billing entities this integration is configured against. Handles are the durable
/// identifiers - the provider reassigns numeric ids whenever the catalog is re-seeded.
/// </summary>
public class SubscriptionSettings
{
    public const string CONFIG_SECTION = "Maxio";

    /// <summary>The product family that holds the plans and the metered component.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>The plan a customer is enrolled in when no plan is named explicitly.</summary>
    public string DefaultProductHandle { get; set; } = string.Empty;

    /// <summary>The second plan, used as the upgrade / downgrade target.</summary>
    public string AlternateProductHandle { get; set; } = string.Empty;

    /// <summary>The metered component that pay-as-you-go usage is reported against.</summary>
    public string MeteredComponentHandle { get; set; } = string.Empty;
}

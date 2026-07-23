namespace Microsoft.eShopWeb;

/// <summary>
/// The billing entities the subscription feature operates against, bound from the same
/// configuration section the Infrastructure client binds (mirrors <see cref="CatalogSettings"/>).
/// Handles are the durable identifiers; the numeric ids the provider assigns are not stable across
/// a re-seed, so everything resolves by handle at runtime.
/// </summary>
public class SubscriptionSettings
{
    /// <summary>The product family holding the plans and the metered component, e.g. <c>eshop-subscribe</c>.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>The plan offered by default, e.g. <c>eshop-pro</c>.</summary>
    public string DefaultProductHandle { get; set; } = string.Empty;

    /// <summary>The alternate plan customers can move to, e.g. <c>basic-plan</c>.</summary>
    public string AlternateProductHandle { get; set; } = string.Empty;

    /// <summary>The metered component usage is billed against, e.g. <c>api-call</c>.</summary>
    public string MeteredComponentHandle { get; set; } = string.Empty;
}

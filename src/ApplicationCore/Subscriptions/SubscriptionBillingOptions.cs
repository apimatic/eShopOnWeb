namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Billing catalog settings bound from the <c>Maxio</c> configuration section.
/// </summary>
public class SubscriptionBillingOptions
{
    public const string CONFIG_SECTION = "Maxio";

    public string ProductFamilyHandle { get; set; } = string.Empty;
}

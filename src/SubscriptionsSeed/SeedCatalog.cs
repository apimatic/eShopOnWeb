using System.Collections.Generic;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// The shape UC0 provisions: one product family, the recurring plans inside it, and the metered
/// component that lives on the family and is therefore available to every subscription on either plan.
/// Handles are the durable identifiers; the numeric ids the provider assigns are not, so nothing here
/// records them.
/// </summary>
internal static class SeedCatalog
{
    public const string ProductFamilyName = "eShopSubscribe";

    /// <summary>Price per unit for the metered component, in dollars.</summary>
    public const string MeteredUnitPrice = "0.01";

    public const string MeteredComponentName = "API Calls";
    public const string MeteredComponentUnitName = "call";

    public static IReadOnlyList<SeedPlan> Plans { get; } = new[]
    {
        new SeedPlan("eshop-pro", "Pro Plan", "eShopOnWeb Pro subscription — the full recurring plan.", 29900),
        new SeedPlan("basic-plan", "Basic Plan", "eShopOnWeb Basic subscription — the entry-level recurring plan.", 2900)
    };
}

/// <summary>
/// A recurring plan to provision. <paramref name="PriceInCents"/> is in minor units because that is the
/// magnitude the provider's product model uses.
/// </summary>
/// <param name="Handle">The durable identifier, e.g. <c>eshop-pro</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Required by the provider on create, even when the business has none.</param>
/// <param name="PriceInCents">Monthly price in cents, e.g. 29900 for $299.00.</param>
internal record SeedPlan(string Handle, string Name, string Description, long PriceInCents);

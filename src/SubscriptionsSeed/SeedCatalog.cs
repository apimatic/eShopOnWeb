namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// The exact shape UC0 requires on the billing provider (plan.md §1.3, UC0 main success scenario).
/// </summary>
/// <remarks>
/// Handles come from configuration so a sandbox can be seeded under different names; the names,
/// prices and kinds are the specification itself and therefore live here.
/// </remarks>
internal static class SeedCatalog
{
    internal const string ProductFamilyName = "eShopSubscribe";

    internal static readonly SeedPlan PrimaryPlan = new(
        Name: "Pro Plan",
        Description: "Everything in Basic, plus priority support and higher limits.",
        PriceInCents: 29_900);

    internal static readonly SeedPlan AlternatePlan = new(
        Name: "Basic Plan",
        Description: "The essentials, billed monthly.",
        PriceInCents: 2_900);

    internal static readonly SeedComponent MeteredComponent = new(
        Name: "API Calls",
        UnitName: "api_call",
        // Dollars, not cents: Maxio takes a component's unit price in currency units.
        UnitPrice: "0.01");

    /// <summary>A recurring plan to seed. Price is in minor units, as Maxio's product API expects.</summary>
    internal sealed record SeedPlan(string Name, string Description, long PriceInCents)
    {
        internal decimal PriceInDollars => PriceInCents / 100m;
    }

    /// <summary>A metered component to seed. Unit price is in whole currency units.</summary>
    internal sealed record SeedComponent(string Name, string UnitName, string UnitPrice);
}

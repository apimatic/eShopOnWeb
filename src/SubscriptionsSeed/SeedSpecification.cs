namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// The shape UC0 provisions: one product family holding two recurring plans and one metered
/// component. Handles are the durable identifiers; Maxio assigns the ids.
/// </summary>
internal sealed record SeedSpecification(
    string FamilyName,
    string FamilyHandle,
    PlanSpecification DefaultPlan,
    PlanSpecification AlternatePlan,
    ComponentSpecification MeteredComponent);

internal sealed record PlanSpecification(
    string Name,
    string Handle,
    string Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

internal sealed record ComponentSpecification(
    string Name,
    string Handle,
    string UnitName,
    string UnitPrice);

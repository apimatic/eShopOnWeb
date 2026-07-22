namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The prorated cost of moving a subscription to another plan, as quoted by the billing provider
/// before anything is committed. All amounts are in major currency units (for example dollars).
/// </summary>
public sealed record PlanChangePreview(
    string CurrentPlanHandle,
    string TargetPlanHandle,
    PlanChangeTiming Timing,
    decimal ProratedAdjustment,
    decimal Charge,
    decimal PaymentDue,
    decimal CreditApplied,
    decimal TargetPlanPrice);

/// <summary>The raw proration figures returned by the provider, in major currency units.</summary>
public sealed record PlanMigrationQuote(
    decimal ProratedAdjustment,
    decimal Charge,
    decimal PaymentDue,
    decimal CreditApplied);

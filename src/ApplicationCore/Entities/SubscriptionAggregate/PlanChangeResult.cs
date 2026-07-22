namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The committed outcome of a plan change (UC3 step 6): old plan, new plan, the amount actually applied,
/// and the subscription as the provider now reports it.
/// </summary>
public class PlanChangeResult
{
    public PlanChangeResult(CustomerSubscription subscription,
        string? previousPlanHandle,
        string? previousPlanName,
        string targetPlanHandle,
        string targetPlanName,
        PlanChangeTiming timing,
        decimal amountApplied)
    {
        Subscription = subscription;
        PreviousPlanHandle = previousPlanHandle;
        PreviousPlanName = previousPlanName;
        TargetPlanHandle = targetPlanHandle;
        TargetPlanName = targetPlanName;
        Timing = timing;
        AmountApplied = amountApplied;
    }

    public CustomerSubscription Subscription { get; }

    public string? PreviousPlanHandle { get; }

    public string? PreviousPlanName { get; }

    public string TargetPlanHandle { get; }

    public string TargetPlanName { get; }

    public PlanChangeTiming Timing { get; }

    /// <summary>The prorated amount charged or credited, matching the confirmed preview.</summary>
    public decimal AmountApplied { get; }
}

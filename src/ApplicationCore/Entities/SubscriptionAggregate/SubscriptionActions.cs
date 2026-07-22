namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>The names of the lifecycle actions, used in transition-error messages (UC4).</summary>
public static class SubscriptionActions
{
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Cancel = "cancel";
    public const string Reactivate = "reactivate";
    public const string ChangePlan = "change-plan";
    public const string RecordUsage = "record-usage";
}

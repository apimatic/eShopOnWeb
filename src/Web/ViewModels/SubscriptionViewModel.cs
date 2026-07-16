namespace Microsoft.eShopWeb.Web.ViewModels;

public class SubscriptionViewModel
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price => PriceInCents / 100m;
    public long PriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }

    public bool CanPause { get; set; }
    public bool CanResume { get; set; }
    public bool CanCancel { get; set; }
    public bool CanReactivate { get; set; }
    public bool CanChangePlan { get; set; }

    /// <summary>The one other configured plan this subscription could move to (UC3: eshop-pro &lt;-&gt; basic-plan).</summary>
    public string? AlternatePlanHandle { get; set; }

    public PlanChangePreviewViewModel? PendingPlanChangePreview { get; set; }
}

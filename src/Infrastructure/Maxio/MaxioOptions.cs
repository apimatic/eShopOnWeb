namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = "";
    public string Subdomain { get; set; } = "";
    public string ProductFamilyHandle { get; set; } = "";
    public string? BaseUrl { get; set; }
}

public sealed class BillingRuntimeLimits
{
    public int MaxPlanPages { get; init; } = 25;
    public int PlanPageSize { get; init; } = 200;
    public System.TimeSpan StaleClaimWindow { get; init; } = System.TimeSpan.FromSeconds(60);
    public System.TimeSpan CallBudget { get; init; } = System.TimeSpan.FromSeconds(20);
}

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetSubscriptionPlansAsync();
    Task<SubscriptionResultDto> SubscribeAsync(string userReference, string email, string firstName, string lastName, string productHandle);
    Task<IReadOnlyList<SubscriptionResultDto>> GetMySubscriptionsAsync(string userReference);
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string Currency { get; set; } = "USD";
    public string IntervalUnit { get; set; } = "month";
    public int Interval { get; set; } = 1;
}

public class SubscriptionResultDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string Currency { get; set; } = "USD";
    public string NextBillingDate { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
}

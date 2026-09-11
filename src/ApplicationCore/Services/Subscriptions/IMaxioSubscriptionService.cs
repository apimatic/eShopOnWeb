using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync();
    Task<IReadOnlyList<MySubscriptionDto>> GetMySubscriptionsAsync(string userEmail);
    Task<MySubscriptionDto> SubscribeAsync(string userEmail, string planHandle);
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; }
    public string Name { get; set; }
    public decimal PriceInCents { get; set; }
    public string Period { get; set; }
    public string Unit { get; set; }
}

public class MySubscriptionDto
{
    public int? Id { get; set; }
    public string PlanHandle { get; set; }
    public string PlanName { get; set; }
    public decimal PriceInCents { get; set; }
    public string State { get; set; }
    public string NextBillingDate { get; set; }
}

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioBillingService
{
    Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync();
    Task<SubscriptionResultDto> SubscribeAsync(string userReference, string planHandle, string email, string firstName, string lastName);
    Task<List<SubscriptionResultDto>> GetMySubscriptionsAsync(string userReference);
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal PriceInCents { get; set; }
    public string PriceFormatted => $"{PriceInCents/100:C}";
    public string State { get; set; } = "";
    public string ProductFamilyHandle { get; set; } = "";
}

public class SubscriptionResultDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string PlanHandle { get; set; } = "";
    public decimal PriceInCents { get; set; }
    public string PriceFormatted => $"{PriceInCents/100:C}";
    public System.DateTime? NextBillingAt { get; set; }
    public string CustomerReference { get; set; } = "";
}

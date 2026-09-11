using System.Threading.Tasks;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<List<PlanDto>> GetSubscriptionPlansAsync();
    Task<SubscriptionDto> CreateSubscriptionAsync(string productHandle, string customerReference, string firstName, string lastName, string email);
    Task<List<SubscriptionDto>> GetMySubscriptionsAsync(string customerReference);
}

public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string FamilyHandle { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string CurrentPeriodEndsAt { get; set; } = string.Empty;
    public string NextBillingAt { get; set; } = string.Empty;
    public decimal BalanceInCents { get; set; }
    public decimal ProductPriceInCents { get; set; }
}

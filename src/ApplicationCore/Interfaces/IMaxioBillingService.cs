using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingService
{
    Task<List<SubscriptionPlanDto>> GetPlansAsync();
    Task<SubscriptionResultDto> SubscribeAsync(string userReference, string email, string productHandle);
    Task<List<MySubscriptionDto>> GetMySubscriptionsAsync(string userReference);
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class SubscriptionResultDto
{
    public bool Created { get; set; }
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string NextBillingDate { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
}

public class MySubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string NextBillingDate { get; set; } = string.Empty;
}

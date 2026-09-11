using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public interface ISubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetPlansAsync();
    Task<List<MySubscriptionDto>> GetMySubscriptionsAsync(string userReference);
    Task<SubscriptionResultDto> SubscribeAsync(string userReference, string email, string planHandle);
}

public record SubscriptionPlanDto(string Handle, string Name, decimal Price, string IntervalUnit);
public record MySubscriptionDto(string PlanHandle, string PlanName, string State, string NextBillingDate);
public record SubscriptionResultDto(bool Success, string Message, string? PlanHandle, string? State, string? NextBillingDate);

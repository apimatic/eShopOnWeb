using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingService
{
    Task<SubscriptionPlanDto> GetPlanByHandleAsync(string handle, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto> CreateSubscriptionAsync(string userReference, string planHandle, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userReference, CancellationToken ct = default);
}

public record SubscriptionPlanDto(string Handle, int Id, string Name, decimal Price);
public record SubscriptionDto(int Id, string State, string PlanHandle, decimal Price, string NextBillingDate);

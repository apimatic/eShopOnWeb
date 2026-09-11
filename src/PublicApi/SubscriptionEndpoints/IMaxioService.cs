using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default);
    Task<SubscriptionPlanDto?> GetPlanByHandleAsync(string handle, CancellationToken ct = default);
    Task<CreateSubscriptionResult> CreateSubscriptionAsync(string productHandle, string customerReference, string firstName, string lastName, string email, CancellationToken ct = default);
    Task<IReadOnlyList<MySubscriptionDto>> ListMySubscriptionsAsync(string customerReference, CancellationToken ct = default);
}

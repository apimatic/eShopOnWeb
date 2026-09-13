using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<IReadOnlyList<PlanDto>> GetPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto> CreateSubscriptionAsync(string userReference, string email, string firstName, string lastName, string productHandle, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userReference, CancellationToken ct = default);
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public interface IBillingService
{
    Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct);
    Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle, CancellationToken ct);
    Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> SwitchPlanAsync(string userName, int subscriptionId, string? planHandle, CancellationToken ct);
    Task<UsageDto> GetUsageAsync(string userName, int subscriptionId, string? date, CancellationToken ct);

    Task<PlanDto> CreatePlanAsync(string? name, decimal? monthlyPrice, int? brandId, string? brand, CancellationToken ct);
    Task<CurrencyPricesDto> AddCurrencyPriceAsync(int planId, string? currency, decimal? price, CancellationToken ct);
    Task<NextBillingDateDto> SetNextBillingDateAsync(int subscriptionId, string? date, CancellationToken ct);
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Subscription billing, with Maxio as the billing system of record.
/// Every failure the application cannot recover from is a <see cref="BillingException"/> (4xx/5xx + reason).
/// </summary>
public interface IMaxioBillingService
{
    Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct);
    Task<SubscriptionDto> SubscribeAsync(string userName, string planHandle, CancellationToken ct);
    Task<SubscriptionDto> GetSubscriptionAsync(string userName, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> SwitchPlanAsync(string userName, int subscriptionId, string planHandle, CancellationToken ct);
    Task<UsageDto> GetUsageAsync(string userName, int subscriptionId, DateOnly date, CancellationToken ct);

    Task<PlanDto> CreatePlanAsync(string name, decimal monthlyPrice, int brandId, string? description, CancellationToken ct);
    Task<CurrencyPriceDto> SetCurrencyPriceAsync(int planId, string currency, decimal price, CancellationToken ct);
    Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, string date, CancellationToken ct);
}

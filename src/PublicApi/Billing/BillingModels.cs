using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public record PlanDto(int Id, string Name, string Handle, string? Description, decimal Price, long PriceInCents,
    int? Interval, string? IntervalUnit, string? Brand, bool Archived);

public record SubscriptionDto(int Id, string? State, bool Paid, long BalanceInCents, PlanDto? Plan,
    DateTimeOffset? NextBillingAt, DateTimeOffset? CanceledAt, DateTimeOffset? CreatedAt, string? Currency);

public record UsageEntryDto(long? Id, decimal Quantity, string? Memo, DateTimeOffset? RecordedAt);

public record ComponentUsageDto(int ComponentId, string? Name, string? UnitName, decimal TotalQuantity,
    IReadOnlyList<UsageEntryDto> Usages);

public record DailyUsageDto(int SubscriptionId, string Date, IReadOnlyList<ComponentUsageDto> Components);

public record CurrencyPriceDto(int PlanId, string Currency, decimal Price);

public interface IBillingService
{
    Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct);
    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string user, string? email, CancellationToken ct);
    Task<SubscriptionDto> SubscribeAsync(string user, string? email, string planHandle, CancellationToken ct);
    Task<SubscriptionDto> GetSubscriptionAsync(string user, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> PauseAsync(string user, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> ResumeAsync(string user, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> CancelAsync(string user, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> RestartAsync(string user, int subscriptionId, CancellationToken ct);
    Task<SubscriptionDto> SwitchPlanAsync(string user, int subscriptionId, string planHandle, CancellationToken ct);
    Task<DailyUsageDto> GetUsageAsync(string user, int subscriptionId, DateOnly date, CancellationToken ct);
    Task<PlanDto> CreatePlanAsync(string name, decimal monthlyPrice, string brandName, string? handle, string? description, CancellationToken ct);
    Task<CurrencyPriceDto> AddCurrencyPriceAsync(int planId, string currency, decimal price, CancellationToken ct);
    Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, DateOnly date, CancellationToken ct);
}

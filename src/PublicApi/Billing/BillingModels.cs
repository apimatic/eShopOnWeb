using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public record PlanDto(int Id, string? Handle, string? Name, decimal MonthlyPrice, string? Brand);

public record SubscribeRequest(string? PlanHandle);

public record SwitchPlanRequest(string? PlanHandle);

public record SubscriptionDto(
    int Id,
    string? State,
    bool Paid,
    decimal AmountOwed,
    int? PlanId,
    string? PlanHandle,
    string? PlanName,
    decimal? MonthlyPrice,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CanceledAt);

public record UsageEntryDto(long? Id, decimal Quantity, string? Memo, DateTimeOffset? RecordedAt);

public record ComponentUsageDto(int ComponentId, string? Name, string? UnitName, decimal Quantity, IReadOnlyList<UsageEntryDto> Usages);

public record DailyUsageDto(int SubscriptionId, string Date, IReadOnlyList<ComponentUsageDto> Components);

public record CreatePlanRequest(string? Name, decimal? MonthlyPrice, int? BrandId, string? Brand);

public record CurrencyPriceRequest(string? Currency, decimal? Price);

public record CurrencyPriceDto(int PlanId, string Currency, decimal Price);

public record NextBillingDateRequest(DateOnly? Date);

public record NextBillingDateDto(int SubscriptionId, string NextBillingDate, DateTimeOffset? NextBillingAt);

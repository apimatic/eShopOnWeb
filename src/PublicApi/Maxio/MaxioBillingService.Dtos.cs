using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public record PlanDto(int PlanId, string? Handle, string? Name, string? Description, long? PriceInCents, decimal? Price, int? Interval, string? IntervalUnit, int? BrandId, string? Brand);

public record PlanListResult(List<PlanDto> Plans, bool Truncated);

public record SubscriptionDto(int SubscriptionId, string? State, string? PlanHandle, string? PlanName, int? PlanId,
    long BalanceInCents, bool Paid, bool PaidDetermined, DateTimeOffset? CurrentPeriodEndsAt, DateTimeOffset? NextAssessmentAt, DateTimeOffset? OnHoldAt);

public record UsageEntryDto(long? Id, string? Quantity, string? Memo, DateTimeOffset? CreatedAt);

public record ComponentUsageDto(int? ComponentId, string? Handle, string? Name, string? UnitName, decimal TotalQuantity, bool QuantityUnparsable, bool Truncated, List<UsageEntryDto> Usages);

public record UsageDayDto(int SubscriptionId, string Date, bool Truncated, List<ComponentUsageDto> Components);

public record CreatedPlanDto(int? PlanId, string? Handle, string? Name, decimal Price, int BrandId, string Brand);

public record CurrencyPriceDto(int PlanId, string Currency, decimal Price, string Action);

public record NextBillingDto(int SubscriptionId, DateTimeOffset? RequestedNextBillingDate, DateTimeOffset? CurrentPeriodEndsAt, DateTimeOffset? NextAssessmentAt);

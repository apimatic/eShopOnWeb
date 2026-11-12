using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public record PlanDto(int Id, string Handle, string Name, string? Description, decimal MonthlyPrice, string Brand, int BrandId);

public record PlanListDto(List<PlanDto> Plans, bool Truncated);

public record SubscriptionDto(
    int SubscriptionId, string State, string? PlanHandle, string? PlanName, bool? Paid,
    DateTimeOffset? NextBillingAt, string? NextPlanHandle);

public record UsageRecordDto(long? Id, decimal Quantity, string? Memo, DateTimeOffset? RecordedAt);

public record ComponentUsageDto(int ComponentId, string? Handle, string? Name, string? UnitName, decimal TotalQuantity, List<UsageRecordDto> Records, bool Truncated);

public record UsageDayDto(int SubscriptionId, string Date, List<ComponentUsageDto> Components, bool Truncated);

public record CurrencyPriceDto(int PlanId, string Currency, decimal Price, string? FormattedPrice);

public record SubscribeRequest(string? PlanHandle);

public record SwitchPlanRequest(string? PlanHandle);

public record CreatePlanRequest(string? Name, decimal? Price, string? Brand, int? BrandId);

public record CurrencyPriceRequest(string? Currency, decimal? Price);

public record NextBillingDateRequest(string? Date);

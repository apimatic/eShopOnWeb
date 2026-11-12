using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public record PlanDto(int Id, string Handle, string Name, decimal MonthlyPrice, int? BrandId, string? Brand);

public record ListPlansResponse(IReadOnlyList<PlanDto> Plans, bool Truncated);

public record SubscriptionDto(int Id, string? PlanHandle, string? PlanName, string? State, bool? Paid,
    long? BalanceInCents, DateTimeOffset? CurrentPeriodEndsAt);

public record UsageEntryDto(long? Id, string? ComponentHandle, int? ComponentId, string? Quantity, string? Memo, DateTimeOffset? RecordedAt);

public record UsageResponseDto(int SubscriptionId, string Date, IReadOnlyList<UsageEntryDto> Usage, bool Truncated);

public record SubscribeRequest(string? PlanHandle);

public record SwitchPlanRequest(string? PlanHandle);

public record CreatePlanRequest(string? Name, decimal MonthlyPrice, int BrandId);

public record CurrencyPriceRequest(string? Currency, decimal Price);

public record CurrencyPriceDto(string? Currency, double? Price, string? FormattedPrice);

public record NextBillingDateRequest(string? Date);

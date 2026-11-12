using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public record PlanDto(int? Id, string? Handle, string? Name, decimal? MonthlyPrice, string? Brand);

public record ListPlansResponse(List<PlanDto> Plans);

public record SubscribeRequest(string? PlanHandle);

public record SwitchPlanRequest(string? PlanHandle);

public record SubscriptionDto(int Id, string? PlanHandle, string? PlanName, string? State, bool Paid, DateTimeOffset? CurrentPeriodEndsAt);

public record UsageRecordDto(long? Id, string? Memo, decimal Quantity, DateTimeOffset? CreatedAt);

public record ComponentUsageDto(int? ComponentId, string? ComponentHandle, string? Name, decimal TotalQuantity, List<UsageRecordDto> Records);

public record DayUsageResponse(int SubscriptionId, string Date, List<ComponentUsageDto> Components);

public record CreatePlanRequest(string? Name, decimal? MonthlyPrice, string? Brand);

public record CurrencyPriceRequest(string? Currency, decimal? Price);

public record CurrencyPriceDto(int PlanId, string Currency, decimal Price);

public record NextBillingDateRequest(DateOnly? Date);

public record NextBillingDateDto(int SubscriptionId, DateTimeOffset NextBillingAt);

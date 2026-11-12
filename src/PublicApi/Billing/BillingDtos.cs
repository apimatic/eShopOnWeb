using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public record PlanDto(int Id, string Handle, string Name, string? Description, string? Brand, decimal MonthlyPrice, string Currency, int Interval, string IntervalUnit);

public record SubscriptionDto(
    int Id,
    string State,
    string? PlanHandle,
    string? PlanName,
    int? PlanId,
    bool Paid,
    decimal BalanceDue,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CanceledAt);

public record UsageComponentDto(int ComponentId, string? Name, string? UnitName, decimal Quantity);

public record UsageDto(int SubscriptionId, string Date, decimal TotalQuantity, IReadOnlyList<UsageComponentDto> Components);

public record CurrencyPriceDto(int PlanId, string Currency, decimal Price);

public record NextBillingDateDto(int SubscriptionId, string NextBillingDate);

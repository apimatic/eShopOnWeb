using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public record BrandRef(int Id, string Name);

public record PlanDto(int PlanId, string Handle, string Name, long PriceInCents, decimal Price, BrandRef? Brand);

public record SubscriptionDto(
    int SubscriptionId,
    string? State,
    string? PlanHandle,
    string? PlanName,
    bool Paid,
    long BalanceInCents,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CanceledAt);

public record UsageComponentDto(int ComponentId, string? Name, decimal Quantity, int Records);

public record UsageDto(int SubscriptionId, string Date, IReadOnlyList<UsageComponentDto> Components, bool Truncated, IReadOnlyList<string> TruncatedComponents, IReadOnlyList<string> Unparseable);

public record CurrencyPriceDto(int PlanId, string Currency, decimal Price, string? FormattedPrice);

public record SubscribeRequest(string? PlanHandle);
public record SwitchRequest(string? PlanHandle);
public record CreatePlanRequest(string? Name, decimal? MonthlyPrice, int? CatalogBrandId);
public record CurrencyPriceRequest(string? Currency, decimal? Price);
public record NextBillingDateRequest(string? Date);

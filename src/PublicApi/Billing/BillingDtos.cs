using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public record PlanDto(int Id, string Handle, string Name, string? Description, string? Brand, decimal MonthlyPrice, long PriceInCents);

public record SubscriptionDto(
    int Id,
    string? State,
    bool Paid,
    long BalanceInCents,
    string? Currency,
    PlanDto? Plan,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? CanceledAt);

public record UsageEntryDto(long? Id, DateTimeOffset? RecordedAt, decimal Quantity, string? Memo);

public record ComponentUsageDto(int ComponentId, string? Name, string? Unit, decimal TotalQuantity, IReadOnlyList<UsageEntryDto> Entries);

public record UsageDayDto(int SubscriptionId, string Date, IReadOnlyList<ComponentUsageDto> Components);

public record CurrencyPriceDto(int PlanId, string Currency, decimal Price, string Role);

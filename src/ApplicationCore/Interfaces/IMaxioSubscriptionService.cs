using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionPlanDto(
    int Id,
    string Handle,
    string Name,
    string Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public record SubscriptionDto(
    int Id,
    string State,
    string? ProductHandle,
    long ProductPriceInCents,
    DateTime? NextBillingAt,
    DateTime? ActivatedAt,
    string? Reference);

public interface IMaxioSubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto> SubscribeAsync(string userId, string planHandle, CancellationToken ct = default);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default);
}

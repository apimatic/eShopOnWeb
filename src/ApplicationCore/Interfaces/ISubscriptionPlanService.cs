using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionPlanService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default);
    Task<SubscriptionResultDto> SubscribeUserAsync(string userId, string productHandle, CancellationToken ct = default);
    Task<IReadOnlyList<UserSubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default);
}

public record SubscriptionPlanDto(
    int Id,
    string Name,
    string Handle,
    string Description,
    decimal Price,
    string IntervalUnit,
    int Interval);

public record SubscriptionResultDto(
    int SubscriptionId,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? NextAssessmentAt,
    string ProductName);

public record UserSubscriptionDto(
    int SubscriptionId,
    string State,
    string ProductName,
    decimal Price,
    DateTimeOffset CreatedAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? CanceledAt);

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionPlanDto(int Id, string Handle, string Name, long PriceInCents);

public record SubscriptionDto(
    int Id,
    string? Reference,
    string State,
    long? ProductPriceInCents,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt);

public interface IMaxioSubscriptionService
{
    Task<IEnumerable<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto> CreateSubscriptionAsync(string userReference, string userEmail, string firstName, string lastName, string productHandle, CancellationToken ct = default);
    Task<IEnumerable<SubscriptionDto>> GetUserSubscriptionsAsync(string userReference, CancellationToken ct = default);
}

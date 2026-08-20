using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int BillingInterval,
    string BillingIntervalUnit,
    bool PaymentMethodRequired);

public sealed record SubscriptionDto(
    int Id,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    int BillingInterval,
    string BillingIntervalUnit,
    string State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt);

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(
        ApplicationUser user,
        string productHandle,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken);
}

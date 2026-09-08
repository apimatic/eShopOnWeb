using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace PublicApiIntegrationTests;

public class StubMaxioSubscriptionService : IMaxioSubscriptionService
{
    public IReadOnlyList<SubscriptionPlanDto>? Plans { get; set; }

    public IReadOnlyList<SubscriptionDto>? Subscriptions { get; set; }

    public SubscribeResult? SubscribeOutcome { get; set; }

    public Exception? SubscribeException { get; set; }

    public MaxioCustomerProfile? LastProfile { get; private set; }

    public string? LastProductHandle { get; private set; }

    public string? LastUserName { get; private set; }

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Plans ?? Array.Empty<SubscriptionPlanDto>());
    }

    public Task<SubscribeResult> SubscribeAsync(MaxioCustomerProfile profile, string productHandle, CancellationToken cancellationToken)
    {
        LastProfile = profile;
        LastProductHandle = productHandle;
        if (SubscribeException is not null)
        {
            throw SubscribeException;
        }

        return Task.FromResult(SubscribeOutcome ?? new SubscribeResult());
    }

    public Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        LastUserName = userName;
        return Task.FromResult(Subscriptions ?? Array.Empty<SubscriptionDto>());
    }
}

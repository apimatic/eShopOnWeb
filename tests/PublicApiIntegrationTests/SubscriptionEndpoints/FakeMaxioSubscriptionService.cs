using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// In-memory stand-in for MaxioSubscriptionService with the same observable
/// endpoint behaviour: idempotent enrollment, unknown-plan 404, per-user lists.
/// </summary>
public class FakeMaxioSubscriptionService : IMaxioSubscriptionService
{
    public int SubscribeCallCount;
    public string? LastUsername;
    public string? LastPlanHandle;

    private static readonly Dictionary<string, SubscriptionPlanDto> Plans = new()
    {
        ["eshop-pro"] = new SubscriptionPlanDto { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
        ["basic-plan"] = new SubscriptionPlanDto { Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" }
    };

    private readonly Dictionary<string, SubscriptionDto> _subscriptions = new();

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<SubscriptionPlanDto>>(Plans.Values.ToList());
    }

    public Task<SubscriptionDto> SubscribeAsync(string username, string planHandle, CancellationToken ct = default)
    {
        SubscribeCallCount++;
        LastUsername = username;
        LastPlanHandle = planHandle;

        if (string.IsNullOrWhiteSpace(planHandle) || !Plans.ContainsKey(planHandle))
        {
            throw new MaxioApiException(404, $"Unknown subscription plan '{planHandle}'.");
        }

        var key = $"{username.ToLowerInvariant()}|{planHandle.ToLowerInvariant()}";
        if (_subscriptions.TryGetValue(key, out var existing))
        {
            return Task.FromResult(existing with { AlreadySubscribed = true });
        }

        var created = new SubscriptionDto
        {
            SubscriptionId = _subscriptions.Count + 94200000,
            PlanHandle = planHandle,
            PlanName = Plans[planHandle].Name,
            PriceInCents = Plans[planHandle].PriceInCents,
            Currency = "USD",
            State = "active",
            NextBillingAtUtc = DateTimeOffset.UtcNow.AddMonths(1),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            AlreadySubscribed = false
        };
        _subscriptions[key] = created;
        return Task.FromResult(created);
    }

    public Task<IReadOnlyList<SubscriptionDto>> ListForUserAsync(string username, CancellationToken ct = default)
    {
        var items = _subscriptions
            .Where(kv => kv.Key.StartsWith($"{username.ToLowerInvariant()}|", StringComparison.Ordinal))
            .Select(kv => kv.Value)
            .ToList();
        return Task.FromResult<IReadOnlyList<SubscriptionDto>>(items);
    }
}
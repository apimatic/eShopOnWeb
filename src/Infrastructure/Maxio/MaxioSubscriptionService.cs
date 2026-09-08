using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly HashSet<string> NonTerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "past_due", "unpaid", "pending", "on_hold", "soft_delete"
    };

    private readonly IMaxioClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IMaxioClient client, IOptions<MaxioOptions> options, IMemoryCache cache, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        return await _client.GetProductsAsync(_options.ProductFamilyHandle, cancellationToken);
    }

    public async Task<SubscriptionEnrollment> EnrollAsync(MaxioUserInfo user, string? productHandle, CancellationToken cancellationToken = default)
    {
        var plans = await GetPlansAsync(cancellationToken);
        if (plans.Count == 0)
        {
            throw new MaxioApiException(404, new List<string> { $"No subscription plans are available in product family '{_options.ProductFamilyHandle}'." });
        }

        var resolvedHandle = !string.IsNullOrWhiteSpace(productHandle)
            ? productHandle
            : ResolveDefaultPlanHandle(plans);

        var plan = plans.FirstOrDefault(p => p.Handle == resolvedHandle)
            ?? throw new MaxioPlanNotFoundException(resolvedHandle);

        var customer = await _client.EnsureCustomerAsync(user.Reference, user.Email, user.FirstName, user.LastName, cancellationToken);

        var lockHandle = await AcquireUserLockAsync(user.Reference, cancellationToken);
        try
        {
            var existing = await _client.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var activeForPlan = existing.FirstOrDefault(s =>
                s.Product.Handle == plan.Handle && NonTerminalStates.Contains(s.State));

            if (activeForPlan is not null)
            {
                _logger.LogInformation(
                    "User {Reference} already has an active Maxio subscription {SubscriptionId} to plan {PlanHandle}; returning it instead of creating a duplicate",
                    user.Reference, activeForPlan.Id, plan.Handle);

                return new SubscriptionEnrollment
                {
                    Customer = customer,
                    Plan = plan,
                    Subscription = activeForPlan,
                    Created = false
                };
            }

            var subscription = await _client.CreateSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} (plan {PlanHandle}) for user {Reference}",
                subscription.Id, plan.Handle, user.Reference);

            return new SubscriptionEnrollment
            {
                Customer = customer,
                Plan = plan,
                Subscription = subscription,
                Created = true
            };
        }
        finally
        {
            lockHandle.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForUserAsync(MaxioUserInfo user, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(user.Reference, cancellationToken);
        if (customer is null)
        {
            return new List<MaxioSubscription>();
        }

        return await _client.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private string ResolveDefaultPlanHandle(IReadOnlyList<MaxioProduct> plans)
    {
        if (!string.IsNullOrWhiteSpace(_options.DefaultProductHandle))
        {
            return _options.DefaultProductHandle;
        }

        return plans.OrderBy(p => p.PriceInCents).First().Handle;
    }

    private async Task<SemaphoreSlim> AcquireUserLockAsync(string reference, CancellationToken cancellationToken)
    {
        var key = $"maxio-enroll-lock:{reference}";
        var gate = _cache.GetOrCreate(key, entry => new SemaphoreSlim(1, 1))!;
        await gate.WaitAsync(cancellationToken);
        return gate;
    }
}

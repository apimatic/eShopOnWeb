using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new(StringComparer.Ordinal);
    private readonly IMaxioBillingClient _maxio;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IMemoryCache _cache;

    public SubscriptionService(IMaxioBillingClient maxio, UserManager<ApplicationUser> users, IMemoryCache cache)
    {
        _maxio = maxio;
        _users = users;
        _cache = cache;
    }

    public Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync("maxio-subscription-plans", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await _maxio.ListPlansAsync(cancellationToken);
        })!;

    public async Task<SubscribeResult> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userName);
        var plan = (await GetPlansAsync(cancellationToken)).SingleOrDefault(x =>
            string.Equals(x.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
            throw new SubscriptionPlanNotFoundException();

        var key = $"{user.Id}:{plan.Handle}";
        var gate = SubscribeLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var existing = await FindExistingPlanSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
                return new SubscribeResult(existing, false);

            try
            {
                var subscription = await _maxio.CreateSubscriptionAsync(customer.Id, plan.Handle, IdempotencyToken(key), cancellationToken);
                return new SubscribeResult(subscription, true);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // The uniqueness token means another retry owns the result. Read it back from Maxio.
                var subscription = await FindExistingPlanSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
                if (subscription is not null)
                    return new SubscribeResult(subscription, false);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(userName);
        var customer = await _maxio.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        return customer is null
            ? Array.Empty<MaxioSubscription>()
            : await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var found = await _maxio.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (found is not null)
            return found;

        try
        {
            return await _maxio.CreateCustomerAsync(user.Id, user.Email ?? user.UserName ?? user.Id, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            // A competing request may have created the unique customer reference.
            var concurrentCustomer = await _maxio.FindCustomerByReferenceAsync(user.Id, cancellationToken);
            if (concurrentCustomer is not null)
                return concurrentCustomer;

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindExistingPlanSubscriptionAsync(long customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(x =>
            string.Equals(x.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase)
            && x.State is "active" or "trialing" or "pending" or "past_due");
    }

    private async Task<ApplicationUser> GetUserAsync(string userName) =>
        await _users.FindByNameAsync(userName) ?? throw new SubscriptionUserNotFoundException();

    private static string IdempotencyToken(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record SubscribeResult(MaxioSubscription Subscription, bool Created);
public sealed class SubscriptionPlanNotFoundException : Exception { }
public sealed class SubscriptionUserNotFoundException : Exception { }

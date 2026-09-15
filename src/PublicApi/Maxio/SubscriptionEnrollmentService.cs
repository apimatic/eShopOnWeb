using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class SubscriptionEnrollmentService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();
    private readonly IMaxioBillingClient _maxio;

    public SubscriptionEnrollmentService(IMaxioBillingClient maxio) => _maxio = maxio;

    public Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken) => _maxio.ListProductsAsync(cancellationToken);

    public async Task<SubscriptionEnrollment> EnrollAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        var normalizedPlanHandle = planHandle.Trim();
        var customerReference = $"eshoponweb:{user.Id}";
        var mutex = EnrollmentLocks.GetOrAdd($"{customerReference}:{normalizedPlanHandle}", _ => new SemaphoreSlim(1, 1));
        await mutex.WaitAsync(cancellationToken);
        try
        {
            var plans = await _maxio.ListProductsAsync(cancellationToken);
            var plan = plans.SingleOrDefault(product => string.Equals(product.Handle, normalizedPlanHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null || string.IsNullOrWhiteSpace(plan.Handle))
            {
                throw new SubscriptionValidationException("The selected subscription plan is unavailable.");
            }

            var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
            var existing = await FindExistingSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                return new SubscriptionEnrollment(plan, existing, true);
            }

            var reference = $"eshoponweb:{user.Id}:{plan.Handle}";
            try
            {
                var created = await _maxio.CreateSubscriptionAsync(customer.Id, plan.Handle, reference, StableToken(reference), cancellationToken);
                return new SubscriptionEnrollment(plan, created, false);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var duplicate = await FindExistingSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
                if (duplicate is not null)
                {
                    return new SubscriptionEnrollment(plan, duplicate, true);
                }
                throw;
            }
        }
        finally
        {
            mutex.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync($"eshoponweb:{user.Id}", cancellationToken);
        return customer is null ? Array.Empty<MaxioSubscription>() : await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new SubscriptionValidationException("A verified email address is required before subscribing.");
        }

        var nameParts = user.Email.Split('@')[0].Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        var draft = new MaxioCustomerDraft
        {
            FirstName = nameParts.FirstOrDefault() ?? "Shopper",
            LastName = nameParts.Skip(1).FirstOrDefault() ?? "Customer",
            Email = user.Email,
            Reference = reference
        };

        try
        {
            return await _maxio.CreateCustomerAsync(draft, StableToken(reference), cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode is System.Net.HttpStatusCode.UnprocessableEntity or System.Net.HttpStatusCode.Conflict)
        {
            var concurrentlyCreated = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (concurrentlyCreated is not null)
            {
                return concurrentlyCreated;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindExistingSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription => IsLive(subscription.State)
            && string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLive(string state) => state is "active" or "trialing" or "pending" or "awaiting_signup" or "assessing";

    private static string StableToken(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record SubscriptionEnrollment(MaxioProduct Plan, MaxioSubscription Subscription, bool AlreadySubscribed);
public sealed class SubscriptionValidationException : Exception { public SubscriptionValidationException(string message) : base(message) { } }

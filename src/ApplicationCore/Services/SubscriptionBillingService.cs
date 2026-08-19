using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new(StringComparer.Ordinal);

    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    private readonly IMaxioBillingClient _maxio;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(IMaxioBillingClient maxio, IAppLogger<SubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return _maxio.ListPlansAsync(cancellationToken);
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        SubscriberIdentity subscriber,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        Guard.Against.NullOrEmpty(subscriber.UserId, nameof(subscriber.UserId));
        Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));

        var handle = productHandle.Trim();
        var gate = SubscribeLocks.GetOrAdd(subscriber.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await GetOrCreateCustomerAsync(subscriber, cancellationToken);
            var subscriptionReference = BuildSubscriptionReference(subscriber.UserId, handle);

            var existingByReference = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existingByReference != null)
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}.",
                    existingByReference.Id, subscriber.UserId, handle);
                return existingByReference;
            }

            var plans = await _maxio.ListPlansAsync(cancellationToken);
            if (!plans.Any(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase)))
            {
                throw new SubscriptionPlanNotFoundException(handle);
            }

            var current = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var liveMatch = current.FirstOrDefault(s =>
                string.Equals(s.ProductHandle, handle, StringComparison.OrdinalIgnoreCase) &&
                IsLive(s.State));
            if (liveMatch != null)
            {
                _logger.LogInformation("User {UserId} already has live Maxio subscription {SubscriptionId} for plan {Plan}.",
                    subscriber.UserId, liveMatch.Id, handle);
                return liveMatch;
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(customer.Id, handle, subscriptionReference, cancellationToken);
                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}.",
                    created.Id, subscriber.UserId, handle);
                return created;
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var raced = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (raced != null)
                {
                    return raced;
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        Guard.Against.NullOrEmpty(subscriber.UserId, nameof(subscriber.UserId));

        var customer = await _maxio.FindCustomerByReferenceAsync(subscriber.UserId, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<BillingCustomer> GetOrCreateCustomerAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(subscriber.UserId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var email = string.IsNullOrWhiteSpace(subscriber.Email) ? subscriber.UserName : subscriber.Email;
        var (firstName, lastName) = DeriveName(subscriber);

        try
        {
            return await _maxio.CreateCustomerAsync(subscriber.UserId, firstName, lastName, email, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(subscriber.UserId, cancellationToken);
            if (raced != null)
            {
                return raced;
            }

            throw;
        }
    }

    internal static string BuildSubscriptionReference(string userId, string productHandle)
        => $"{userId}:{productHandle}";

    internal static bool IsLive(string state)
        => !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state);

    internal static (string FirstName, string LastName) DeriveName(SubscriberIdentity subscriber)
    {
        var source = subscriber.Email;
        if (string.IsNullOrWhiteSpace(source))
        {
            source = subscriber.UserName;
        }

        var local = source.Contains('@', StringComparison.Ordinal)
            ? source.Split('@')[0]
            : source;

        local = string.IsNullOrWhiteSpace(local) ? "Shopper" : local.Trim();
        return (local, "Shopper");
    }
}

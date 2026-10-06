using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the Maxio subscription capability: browsing plans, provisioning a Maxio customer
/// for an eShopOnWeb user (idempotently), subscribing to a plan (idempotently) and listing the
/// user's subscriptions.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly IMaxioClient _client;
    private readonly IRepository<MaxioCustomerMapping> _customerMappingRepository;
    private readonly IRepository<MaxioSubscriptionMapping> _subscriptionMappingRepository;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;
    private readonly MaxioSettings _settings;

    // Serializes subscribe/customer-provisioning work per user so a double-click can never
    // create two customers or two subscriptions within this process.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    public MaxioSubscriptionService(
        IMaxioClient client,
        IRepository<MaxioCustomerMapping> customerMappingRepository,
        IRepository<MaxioSubscriptionMapping> subscriptionMappingRepository,
        IAppLogger<MaxioSubscriptionService> logger,
        MaxioSettings settings)
    {
        _client = client;
        _customerMappingRepository = customerMappingRepository;
        _subscriptionMappingRepository = subscriptionMappingRepository;
        _logger = logger;
        _settings = settings;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return await _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
    }

    public async Task<MaxioSubscription> SubscribeAsync(MaxioSubscriber subscriber, string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new ArgumentException("A product handle is required.", nameof(productHandle));
        }

        var product = await _client.GetProductByHandleAsync(productHandle, cancellationToken);
        if (product is null)
        {
            throw new MaxioApiException(404, $"No plan with handle '{productHandle}' was found in Maxio.");
        }

        var gate = _userLocks.GetOrAdd(subscriber.UserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

            var existing = await FindExistingSubscriptionAsync(subscriber.UserId, productHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("User {UserName} already has subscription {SubscriptionId} to plan {ProductHandle}; returning the existing subscription.",
                    subscriber.UserName, existing.Id, productHandle);
                return existing;
            }

            var reference = BuildSubscriptionReference(subscriber.UserId, productHandle);
            var uniquenessToken = Guid.NewGuid().ToString();

            MaxioSubscription created;
            try
            {
                created = await _client.CreateSubscriptionAsync(customer.Id, productHandle, reference, uniquenessToken, cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 409 || ex.StatusCode == 422)
            {
                // A concurrent request (or a previous run) already created this subscription.
                _logger.LogWarning("Subscription creation for user {UserName} and plan {ProductHandle} reported a duplicate ({StatusCode}); looking up the existing subscription.",
                    subscriber.UserName, productHandle, ex.StatusCode);
                var found = await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken);
                if (found is not null)
                {
                    await PersistSubscriptionMappingAsync(subscriber.UserId, found, cancellationToken);
                    return found;
                }
                throw;
            }

            await PersistSubscriptionMappingAsync(subscriber.UserId, created, cancellationToken);
            return created;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListMySubscriptionsAsync(MaxioSubscriber subscriber, CancellationToken cancellationToken = default)
    {
        var customer = await EnsureCustomerAsync(subscriber, cancellationToken);
        return await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(MaxioSubscriber subscriber, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(subscriber.UserId);

        var mapping = await _customerMappingRepository.FirstOrDefaultAsync(
            new MaxioCustomerMappingByUserSpecification(subscriber.UserId), cancellationToken);
        if (mapping is not null)
        {
            var customer = await _client.GetCustomerAsync(mapping.MaxioCustomerId, cancellationToken);
            if (customer is not null)
            {
                return customer;
            }
        }

        var byReference = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (byReference is not null)
        {
            await PersistCustomerMappingAsync(subscriber.UserId, byReference, cancellationToken);
            return byReference;
        }

        try
        {
            var draft = new MaxioCustomerDraft
            {
                FirstName = DeriveFirstName(subscriber.Email),
                LastName = "User",
                Email = subscriber.Email,
                Reference = reference
            };
            var created = await _client.CreateCustomerAsync(draft, cancellationToken);
            await PersistCustomerMappingAsync(subscriber.UserId, created, cancellationToken);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // A concurrent request already created the customer; look it up instead.
            _logger.LogWarning("Customer creation for user {UserName} reported a duplicate (422); looking up the existing customer.", subscriber.UserName);
            var found = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (found is not null)
            {
                await PersistCustomerMappingAsync(subscriber.UserId, found, cancellationToken);
                return found;
            }
            throw;
        }
    }

    private async Task<MaxioSubscription?> FindExistingSubscriptionAsync(string userId, string productHandle, CancellationToken cancellationToken)
    {
        var mapping = await _subscriptionMappingRepository.FirstOrDefaultAsync(
            new MaxioSubscriptionMappingByUserAndProductSpecification(userId, productHandle), cancellationToken);
        if (mapping is not null)
        {
            var subscription = await _client.GetSubscriptionAsync(mapping.MaxioSubscriptionId, cancellationToken);
            if (subscription is not null)
            {
                return subscription;
            }
        }

        var reference = BuildSubscriptionReference(userId, productHandle);
        var byReference = await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (byReference is not null)
        {
            await PersistSubscriptionMappingAsync(userId, byReference, cancellationToken);
            return byReference;
        }

        return null;
    }

    private async Task PersistCustomerMappingAsync(string userId, MaxioCustomer customer, CancellationToken cancellationToken)
    {
        var existing = await _customerMappingRepository.FirstOrDefaultAsync(
            new MaxioCustomerMappingByUserSpecification(userId), cancellationToken);
        if (existing is not null)
        {
            return;
        }

        await _customerMappingRepository.AddAsync(
            new MaxioCustomerMapping(userId, customer.Id, customer.Reference ?? string.Empty), cancellationToken);
    }

    private async Task PersistSubscriptionMappingAsync(string userId, MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        var productHandle = subscription.Product?.Handle ?? string.Empty;
        var existing = await _subscriptionMappingRepository.FirstOrDefaultAsync(
            new MaxioSubscriptionMappingByUserAndProductSpecification(userId, productHandle), cancellationToken);
        if (existing is not null)
        {
            return;
        }

        await _subscriptionMappingRepository.AddAsync(
            new MaxioSubscriptionMapping(userId, subscription.Id, subscription.Customer?.Id ?? 0, productHandle), cancellationToken);
    }

    private static string BuildCustomerReference(string userId) => $"eshop-{userId}";

    private static string BuildSubscriptionReference(string userId, string productHandle) => $"eshop-{userId}-{productHandle}";

    private static string DeriveFirstName(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }
}

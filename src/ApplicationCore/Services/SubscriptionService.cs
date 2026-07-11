using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IPublisher _publisher;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IBillingClient billingClient,
        IRepository<Subscription> subscriptionRepository,
        IPublisher publisher,
        IOptions<MaxioSettings> settings,
        ILogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _subscriptionRepository = subscriptionRepository;
        _publisher = publisher;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<List<BillingProduct>> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        return await _billingClient.ListProductsAsync(_settings.ProductFamilyId, cancellationToken);
    }

    public async Task<BillingSubscription> SubscribeAsync(string userId, string email, string firstName, string lastName, int productId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NegativeOrZero(productId, nameof(productId));

        var customerOrNull = await _billingClient.CreateOrGetCustomerAsync(userId, email, firstName, lastName, cancellationToken);

        var existingSubscription = await GetExistingActiveSubscriptionAsync(userId, cancellationToken);
        if (existingSubscription != null && existingSubscription.ProductId == productId)
        {
            _logger.LogInformation($"User {userId} already has an active subscription to product {productId}");
            return existingSubscription;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customerOrNull.Id, productId, cancellationToken);

        var product = await _billingClient.GetProductAsync(productId, cancellationToken);

        var localSubscription = new Subscription(userId, customerOrNull.Id, subscription.Id, productId, product.Handle);
        await _subscriptionRepository.AddAsync(localSubscription, cancellationToken);

        await _publisher.Publish(new SubscriptionActivated(
            localSubscription.Id,
            subscription.Id,
            userId,
            product.Handle,
            productId,
            product.Price,
            product.BillingCycle), cancellationToken);

        return subscription;
    }

    public async Task<BillingSubscription> GetUserSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));

        var localSubscription = await _subscriptionRepository.FirstOrDefaultAsync(
            new SubscriptionsByUserSpecification(userId), cancellationToken);

        if (localSubscription == null || localSubscription.MaxioSubscriptionId != subscriptionId)
        {
            throw new SubscriptionNotFoundException($"Subscription {subscriptionId} not found for user {userId}");
        }

        return await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
    }

    public async Task<List<BillingSubscription>> GetUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));

        var subscriptions = new List<BillingSubscription>();
        var localSubscriptions = await _subscriptionRepository.ListAsync(
            new SubscriptionsByUserSpecification(userId), cancellationToken);

        foreach (var localSub in localSubscriptions)
        {
            try
            {
                var subscription = await _billingClient.GetSubscriptionAsync(localSub.MaxioSubscriptionId, cancellationToken);
                subscriptions.Add(subscription);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to get subscription {localSub.MaxioSubscriptionId}: {ex.Message}");
            }
        }

        return subscriptions;
    }

    public async Task RecordUsageAsync(string userId, int subscriptionId, int componentId, decimal quantity, string? memo = null, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));
        Guard.Against.Negative(quantity, nameof(quantity));

        if (quantity == 0)
        {
            throw new InvalidBillingOperationException("Quantity must be greater than zero");
        }

        await VerifyComponentAsync(componentId, cancellationToken);
        await VerifySubscriptionOwnershipAsync(userId, subscriptionId, cancellationToken);

        await _billingClient.RecordUsageAsync(subscriptionId, componentId, quantity, memo, cancellationToken);
    }

    public async Task<decimal> GetUsageTotalAsync(string userId, int subscriptionId, int componentId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));

        await VerifySubscriptionOwnershipAsync(userId, subscriptionId, cancellationToken);
        return await _billingClient.GetUsageTotalAsync(subscriptionId, componentId, cancellationToken);
    }

    public async Task<BillingSubscription> ChangeSubscriptionPlanAsync(string userId, int subscriptionId, int newProductId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));
        Guard.Against.NegativeOrZero(newProductId, nameof(newProductId));

        await VerifySubscriptionOwnershipAsync(userId, subscriptionId, cancellationToken);

        var currentSubscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (currentSubscription.ProductId == newProductId)
        {
            throw new InvalidBillingOperationException("New product must be different from current product");
        }

        var newProduct = await _billingClient.GetProductAsync(newProductId, cancellationToken);
        var oldProduct = await _billingClient.GetProductAsync(currentSubscription.ProductId, cancellationToken);

        var updatedSubscription = await _billingClient.UpdateSubscriptionAsync(subscriptionId, newProductId, cancellationToken);

        var localSubscription = await _subscriptionRepository.FirstOrDefaultAsync(
            new SubscriptionsByUserSpecification(userId), cancellationToken);

        if (localSubscription != null)
        {
            localSubscription.UpdateMaxioReferences(currentSubscription.CustomerId, updatedSubscription.Id);
            await _subscriptionRepository.UpdateAsync(localSubscription, cancellationToken);
        }

        var proratedAmount = await _billingClient.GetProratedAmountAsync(subscriptionId, newProductId, cancellationToken);

        await _publisher.Publish(new SubscriptionPlanChanged(
            localSubscription?.Id ?? 0,
            updatedSubscription.Id,
            userId,
            oldProduct.Handle,
            newProduct.Handle,
            oldProduct.Price,
            newProduct.Price,
            proratedAmount,
            DateTimeOffset.UtcNow), cancellationToken);

        return updatedSubscription;
    }

    public async Task<decimal> GetProratedAmountAsync(string userId, int subscriptionId, int newProductId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));

        await VerifySubscriptionOwnershipAsync(userId, subscriptionId, cancellationToken);
        return await _billingClient.GetProratedAmountAsync(subscriptionId, newProductId, cancellationToken);
    }

    public async Task PauseSubscriptionAsync(string userId, int subscriptionId, string? reason = null, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));

        await VerifySubscriptionOwnershipAsync(userId, subscriptionId, cancellationToken);

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        await _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken);

        await _publisher.Publish(new SubscriptionStateChanged(
            0, subscriptionId, userId, subscription.State, "on_hold", DateTimeOffset.UtcNow, reason), cancellationToken);
    }

    public async Task ResumeSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));

        await VerifySubscriptionOwnershipAsync(userId, subscriptionId, cancellationToken);

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        await _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken);

        await _publisher.Publish(new SubscriptionStateChanged(
            0, subscriptionId, userId, subscription.State, "active", DateTimeOffset.UtcNow), cancellationToken);
    }

    public async Task CancelSubscriptionAsync(string userId, int subscriptionId, bool cancelImmediately = false, string? reason = null, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));

        await VerifySubscriptionOwnershipAsync(userId, subscriptionId, cancellationToken);

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        await _billingClient.CancelSubscriptionAsync(subscriptionId, cancelImmediately, cancellationToken);

        await _publisher.Publish(new SubscriptionStateChanged(
            0, subscriptionId, userId, subscription.State, "canceled", DateTimeOffset.UtcNow, reason), cancellationToken);
    }

    public async Task ReactivateSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NegativeOrZero(subscriptionId, nameof(subscriptionId));

        await VerifySubscriptionOwnershipAsync(userId, subscriptionId, cancellationToken);

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        await _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken);

        await _publisher.Publish(new SubscriptionStateChanged(
            0, subscriptionId, userId, subscription.State, "active", DateTimeOffset.UtcNow), cancellationToken);
    }

    private async Task VerifySubscriptionOwnershipAsync(string userId, int subscriptionId, CancellationToken cancellationToken)
    {
        var localSubscription = await _subscriptionRepository.FirstOrDefaultAsync(
            new SubscriptionsByUserSpecification(userId), cancellationToken);

        if (localSubscription == null || localSubscription.MaxioSubscriptionId != subscriptionId)
        {
            throw new SubscriptionNotFoundException($"Subscription {subscriptionId} not found for user {userId}");
        }
    }

    private async Task VerifyComponentAsync(int componentId, CancellationToken cancellationToken)
    {
        try
        {
            await _billingClient.GetComponentByHandleAsync(_settings.ProductFamilyId, _settings.MeteredComponentHandle!, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to verify component: {ex.Message}");
            throw new BillingProviderException($"Invalid component configuration: {ex.Message}", ex);
        }
    }

    private async Task<BillingSubscription?> GetExistingActiveSubscriptionAsync(string userId, CancellationToken cancellationToken)
    {
        var subscriptions = await GetUserSubscriptionsAsync(userId, cancellationToken);
        return subscriptions.FirstOrDefault(s => s.State == "active");
    }
}

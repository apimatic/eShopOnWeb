using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IPublisher _publisher;

    public SubscriptionService(IBillingClient billingClient, IRepository<Subscription> subscriptionRepository, IPublisher publisher)
    {
        _billingClient = billingClient;
        _subscriptionRepository = subscriptionRepository;
        _publisher = publisher;
    }

    public async Task<List<BillingProduct>> ListAvailableProductsAsync(CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(_billingClient);
        try
        {
            return await _billingClient.ListProductsAsync(3008866, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Failed to list products from billing provider: {ex.Message}", ex);
        }
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, string userEmail, int productId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(userEmail, nameof(userEmail));
        Guard.Against.Negative(productId, nameof(productId));

        try
        {
            var customer = await _billingClient.GetOrCreateCustomerAsync(userId, userEmail, cancellationToken);
            Guard.Against.Null(customer, nameof(customer), "Failed to create or get customer from billing provider");

            var existingSubscription = await _billingClient.GetSubscriptionByCustomerAndProductAsync(customer.Id, productId, cancellationToken);
            if (existingSubscription != null && existingSubscription.State != "canceled")
            {
                return MapToSubscriptionDto(existingSubscription);
            }

            var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, productId, cancellationToken);
            Guard.Against.Null(subscription, nameof(subscription), "Failed to create subscription");

            var localSubscription = new Subscription(userId, customer.Id, subscription.Handle, subscription.ProductHandle,
                subscription.ProductId, SubscriptionState.Active);
            await _subscriptionRepository.AddAsync(localSubscription, cancellationToken);

            await _publisher.Publish(new SubscriptionActivated(userId, subscription.Id, subscription.ProductHandle,
                subscription.CurrentPrice, subscription.NextBillingDate), cancellationToken);

            return MapToSubscriptionDto(subscription);
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Subscription failed: {ex.Message}", ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));

        var specifications = new SubscriptionsByUserSpecification(userId);
        var subscriptions = await _subscriptionRepository.ListAsync(specifications, cancellationToken);

        return subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            BillingProviderId = s.BillingProviderId,
            BillingProviderSubscriptionHandle = s.BillingProviderSubscriptionHandle,
            ProductHandle = s.ProductHandle,
            ProductId = s.ProductId,
            State = s.State.ToString(),
            CreatedAt = s.CreatedAt
        }).ToList();
    }

    public async Task<SubscriptionDto> GetSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.Negative(subscriptionId, nameof(subscriptionId));

        var subscription = await _subscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken);
        Guard.Against.Null(subscription, nameof(subscription), $"Subscription {subscriptionId} not found");
        Guard.Against.InvalidInput(subscription.UserId, nameof(subscription.UserId), s => s == userId, "Subscription does not belong to user");

        try
        {
            var remoteSubscription = await _billingClient.GetSubscriptionAsync(subscription.BillingProviderId, cancellationToken);
            return MapToSubscriptionDto(remoteSubscription);
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Failed to get subscription details: {ex.Message}", ex);
        }
    }

    public async Task RecordUsageAsync(string userId, int subscriptionId, int componentId, int quantity, string? memo = null, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.Negative(subscriptionId, nameof(subscriptionId));
        Guard.Against.Negative(componentId, nameof(componentId));
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var subscription = await _subscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken);
        Guard.Against.Null(subscription, nameof(subscription), $"Subscription {subscriptionId} not found");
        Guard.Against.InvalidInput(subscription.UserId, nameof(subscription.UserId), s => s == userId, "Subscription does not belong to user");

        try
        {
            await _billingClient.RecordUsageAsync(subscription.BillingProviderId, componentId, quantity, memo, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Failed to record usage: {ex.Message}", ex);
        }
    }

    public async Task<decimal> GetUsageAsync(string userId, int subscriptionId, int componentId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.Negative(subscriptionId, nameof(subscriptionId));
        Guard.Against.Negative(componentId, nameof(componentId));

        var subscription = await _subscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken);
        Guard.Against.Null(subscription, nameof(subscription), $"Subscription {subscriptionId} not found");
        Guard.Against.InvalidInput(subscription.UserId, nameof(subscription.UserId), s => s == userId, "Subscription does not belong to user");

        try
        {
            var result = await _billingClient.GetUsageAsync(subscription.BillingProviderId, componentId, cancellationToken);
            return result.PeriodToDateTotal;
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Failed to get usage: {ex.Message}", ex);
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string userId, int subscriptionId, int newProductId, bool prorationOnChange, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.Negative(subscriptionId, nameof(subscriptionId));
        Guard.Against.Negative(newProductId, nameof(newProductId));

        var subscription = await _subscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken);
        Guard.Against.Null(subscription, nameof(subscription), $"Subscription {subscriptionId} not found");
        Guard.Against.InvalidInput(subscription.UserId, nameof(subscription.UserId), s => s == userId, "Subscription does not belong to user");

        if (subscription.ProductId == newProductId)
        {
            throw new BillingProviderException("Target plan is the same as current plan");
        }

        try
        {
            return await _billingClient.PreviewPlanChangeAsync(subscription.BillingProviderId, newProductId, prorationOnChange, cancellationToken);
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Failed to preview plan change: {ex.Message}", ex);
        }
    }

    public async Task ChangePlanAsync(string userId, int subscriptionId, int newProductId, bool prorationOnChange, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.Negative(subscriptionId, nameof(subscriptionId));
        Guard.Against.Negative(newProductId, nameof(newProductId));

        var subscription = await _subscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken);
        Guard.Against.Null(subscription, nameof(subscription), $"Subscription {subscriptionId} not found");
        Guard.Against.InvalidInput(subscription.UserId, nameof(subscription.UserId), s => s == userId, "Subscription does not belong to user");

        if (subscription.ProductId == newProductId)
        {
            throw new BillingProviderException("Target plan is the same as current plan");
        }

        try
        {
            var oldProductHandle = subscription.ProductHandle;
            await _billingClient.ChangePlanAsync(subscription.BillingProviderId, newProductId, prorationOnChange, cancellationToken);

            var updatedBillingSubscription = await _billingClient.GetSubscriptionAsync(subscription.BillingProviderId, cancellationToken);
            subscription.UpdateProductAndState(updatedBillingSubscription.ProductHandle, updatedBillingSubscription.ProductId, SubscriptionState.Active);
            await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

            await _publisher.Publish(new SubscriptionPlanChanged(userId, subscription.Id, oldProductHandle,
                updatedBillingSubscription.ProductHandle, updatedBillingSubscription.CurrentPrice,
                updatedBillingSubscription.NextBillingDate), cancellationToken);
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Failed to change plan: {ex.Message}", ex);
        }
    }

    public async Task PauseSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default)
    {
        await ChangeSubscriptionStateAsync(userId, subscriptionId, SubscriptionState.Paused,
            (client, id) => client.PauseSubscriptionAsync(id, cancellationToken), "pause", cancellationToken);
    }

    public async Task ResumeSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default)
    {
        await ChangeSubscriptionStateAsync(userId, subscriptionId, SubscriptionState.Active,
            (client, id) => client.ResumeSubscriptionAsync(id, cancellationToken), "resume", cancellationToken);
    }

    public async Task CancelSubscriptionAsync(string userId, int subscriptionId, bool immediate = false, CancellationToken cancellationToken = default)
    {
        await ChangeSubscriptionStateAsync(userId, subscriptionId,
            immediate ? SubscriptionState.Cancelled : SubscriptionState.PendingCancellation,
            (client, id) => client.CancelSubscriptionAsync(id, immediate, cancellationToken), "cancel", cancellationToken);
    }

    public async Task ReactivateSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default)
    {
        await ChangeSubscriptionStateAsync(userId, subscriptionId, SubscriptionState.Active,
            (client, id) => client.ReactivateSubscriptionAsync(id, cancellationToken), "reactivate", cancellationToken);
    }

    private async Task ChangeSubscriptionStateAsync(string userId, int subscriptionId, SubscriptionState newState,
        Func<IBillingClient, int, Task> billingAction, string action, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.Negative(subscriptionId, nameof(subscriptionId));

        var subscription = await _subscriptionRepository.GetByIdAsync(subscriptionId, cancellationToken);
        Guard.Against.Null(subscription, nameof(subscription), $"Subscription {subscriptionId} not found");
        Guard.Against.InvalidInput(subscription.UserId, nameof(subscription.UserId), s => s == userId, "Subscription does not belong to user");

        try
        {
            var oldState = subscription.State;
            await billingAction(_billingClient, subscription.BillingProviderId);

            var updatedBillingSubscription = await _billingClient.GetSubscriptionAsync(subscription.BillingProviderId, cancellationToken);
            var mappedNewState = MapBillingStateToLocal(updatedBillingSubscription.State);

            subscription.UpdateState(mappedNewState);
            await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

            await _publisher.Publish(new SubscriptionStateChanged(userId, subscription.Id, oldState.ToString(),
                mappedNewState.ToString(), updatedBillingSubscription.NextBillingDate, action), cancellationToken);
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Failed to {action} subscription: {ex.Message}", ex);
        }
    }

    private SubscriptionDto MapToSubscriptionDto(BillingSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            BillingProviderId = subscription.CustomerId,
            BillingProviderSubscriptionHandle = subscription.Handle,
            ProductHandle = subscription.ProductHandle,
            ProductId = subscription.ProductId,
            CurrentPrice = subscription.CurrentPrice,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate,
            CreatedAt = subscription.CreatedAt
        };
    }

    private SubscriptionState MapBillingStateToLocal(string billingState)
    {
        return billingState.ToLowerInvariant() switch
        {
            "active" => SubscriptionState.Active,
            "paused" => SubscriptionState.Paused,
            "pending_cancellation" => SubscriptionState.PendingCancellation,
            "canceled" => SubscriptionState.Cancelled,
            _ => SubscriptionState.Active
        };
    }
}

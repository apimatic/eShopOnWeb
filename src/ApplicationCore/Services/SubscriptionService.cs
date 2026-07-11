using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly IPublisher _publisher;

    public SubscriptionService(IBillingClient billingClient, IPublisher publisher)
    {
        _billingClient = Guard.Against.Null(billingClient, nameof(billingClient));
        _publisher = Guard.Against.Null(publisher, nameof(publisher));
    }

    public async Task<List<SubscriptionPlanDto>> ListAvailablePlansAsync()
    {
        var plans = await _billingClient.ListProductsAsync(3008866); // eshop-subscribe family
        return plans
            .OrderBy(p => p.Price)
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                FamilyId = p.FamilyId,
                Name = p.Name,
                Handle = p.Handle,
                Price = p.Price
            })
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, int productId)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.Default(productId, nameof(productId));

        try
        {
            var customer = await _billingClient.GetOrCreateCustomerAsync(userId);
            if (customer == null)
            {
                throw new BillingProviderException($"Failed to get or create customer for {userId}");
            }

            var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, productId);
            var product = await _billingClient.GetProductAsync(productId);

            var dto = new SubscriptionDto
            {
                Id = subscription.Id,
                UserId = userId,
                MaxioSubscriptionId = subscription.Id,
                ProductId = subscription.ProductId,
                ProductHandle = product.Handle,
                State = MapSubscriptionState(subscription.State),
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextBillingAt = subscription.NextBillingAt
            };

            await _publisher.Publish(new SubscriptionActivated
            {
                SubscriptionId = subscription.Id,
                UserId = userId,
                MaxioSubscriptionId = subscription.Id,
                ProductId = productId,
                ProductHandle = product.Handle
            });

            return dto;
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Failed to subscribe user {userId} to product {productId}: {ex.Message}", ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));

        try
        {
            var customer = await _billingClient.GetOrCreateCustomerAsync(userId);
            if (customer == null)
            {
                return new List<SubscriptionDto>();
            }

            var subscriptions = new List<SubscriptionDto>();

            // Note: In a real implementation, we'd query a local database to get the user's subscriptions
            // For now, we return an empty list since we don't persist locally
            return subscriptions;
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Failed to get subscriptions for user {userId}: {ex.Message}", ex);
        }
    }

    public async Task RecordUsageAsync(string userId, int subscriptionId, int componentId, decimal quantity, string? memo = null)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.Default(subscriptionId, nameof(subscriptionId));
        Guard.Against.Default(componentId, nameof(componentId));

        if (quantity <= 0)
        {
            throw new ArgumentException("Quantity must be greater than 0", nameof(quantity));
        }

        try
        {
            await _billingClient.RecordUsageAsync(subscriptionId, componentId, quantity, memo);
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Failed to record usage: {ex.Message}", ex);
        }
    }

    public async Task<UsageDto> GetUsageAsync(string userId, int subscriptionId, int componentId)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.Default(subscriptionId, nameof(subscriptionId));
        Guard.Against.Default(componentId, nameof(componentId));

        try
        {
            var usage = await _billingClient.GetUsageAsync(subscriptionId, componentId);
            return new UsageDto
            {
                CurrentUsage = usage.CurrentUsage,
                UnitPrice = usage.UnitPrice
            };
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Failed to get usage: {ex.Message}", ex);
        }
    }

    public async Task<PlanChangePreviewDto> PreviewPlanChangeAsync(string userId, int subscriptionId, int newProductId)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.Default(subscriptionId, nameof(subscriptionId));
        Guard.Against.Default(newProductId, nameof(newProductId));

        try
        {
            var preview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, newProductId);
            return new PlanChangePreviewDto
            {
                HighestCharge = preview.HighestChargeInTermsOfStatusAmount,
                LowestCharge = preview.LowestChargeInTermsOfStatusAmount,
                ProrationAdjustment = preview.AccruedProrationAdjustmentAmount
            };
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

    public async Task<SubscriptionDto> ChangePlanAsync(string userId, int subscriptionId, int newProductId)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.Default(subscriptionId, nameof(subscriptionId));
        Guard.Against.Default(newProductId, nameof(newProductId));

        try
        {
            var currentSubscription = await _billingClient.GetSubscriptionAsync(subscriptionId);
            var oldProductId = currentSubscription.ProductId;
            var oldProduct = await _billingClient.GetProductAsync(oldProductId);

            var updatedSubscription = await _billingClient.ChangeSubscriptionPlanAsync(subscriptionId, newProductId);
            var newProduct = await _billingClient.GetProductAsync(newProductId);

            await _publisher.Publish(new SubscriptionPlanChanged
            {
                SubscriptionId = subscriptionId,
                UserId = userId,
                MaxioSubscriptionId = subscriptionId,
                OldProductId = oldProductId,
                OldProductHandle = oldProduct.Handle,
                NewProductId = newProductId,
                NewProductHandle = newProduct.Handle
            });

            return new SubscriptionDto
            {
                Id = updatedSubscription.Id,
                UserId = userId,
                MaxioSubscriptionId = updatedSubscription.Id,
                ProductId = updatedSubscription.ProductId,
                ProductHandle = newProduct.Handle,
                State = MapSubscriptionState(updatedSubscription.State)
            };
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

    public async Task<SubscriptionDto> PauseSubscriptionAsync(string userId, int subscriptionId)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.Default(subscriptionId, nameof(subscriptionId));

        try
        {
            var currentSubscription = await _billingClient.GetSubscriptionAsync(subscriptionId);
            var oldState = MapSubscriptionState(currentSubscription.State);

            var pausedSubscription = await _billingClient.PauseSubscriptionAsync(subscriptionId);
            var product = await _billingClient.GetProductAsync(pausedSubscription.ProductId);
            var newState = MapSubscriptionState(pausedSubscription.State);

            await _publisher.Publish(new SubscriptionStateChanged
            {
                SubscriptionId = subscriptionId,
                UserId = userId,
                MaxioSubscriptionId = subscriptionId,
                OldState = oldState,
                NewState = newState
            });

            return new SubscriptionDto
            {
                Id = pausedSubscription.Id,
                UserId = userId,
                MaxioSubscriptionId = pausedSubscription.Id,
                ProductId = pausedSubscription.ProductId,
                ProductHandle = product.Handle,
                State = newState
            };
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Failed to pause subscription: {ex.Message}", ex);
        }
    }

    public async Task<SubscriptionDto> ResumeSubscriptionAsync(string userId, int subscriptionId)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.Default(subscriptionId, nameof(subscriptionId));

        try
        {
            var currentSubscription = await _billingClient.GetSubscriptionAsync(subscriptionId);
            var oldState = MapSubscriptionState(currentSubscription.State);

            var resumedSubscription = await _billingClient.ResumeSubscriptionAsync(subscriptionId);
            var product = await _billingClient.GetProductAsync(resumedSubscription.ProductId);
            var newState = MapSubscriptionState(resumedSubscription.State);

            await _publisher.Publish(new SubscriptionStateChanged
            {
                SubscriptionId = subscriptionId,
                UserId = userId,
                MaxioSubscriptionId = subscriptionId,
                OldState = oldState,
                NewState = newState
            });

            return new SubscriptionDto
            {
                Id = resumedSubscription.Id,
                UserId = userId,
                MaxioSubscriptionId = resumedSubscription.Id,
                ProductId = resumedSubscription.ProductId,
                ProductHandle = product.Handle,
                State = newState
            };
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Failed to resume subscription: {ex.Message}", ex);
        }
    }

    public async Task<SubscriptionDto> CancelSubscriptionAsync(string userId, int subscriptionId, bool atEndOfPeriod = false)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.Default(subscriptionId, nameof(subscriptionId));

        try
        {
            var currentSubscription = await _billingClient.GetSubscriptionAsync(subscriptionId);
            var oldState = MapSubscriptionState(currentSubscription.State);

            var cancelledSubscription = await _billingClient.CancelSubscriptionAsync(subscriptionId, atEndOfPeriod);
            var product = await _billingClient.GetProductAsync(cancelledSubscription.ProductId);
            var newState = MapSubscriptionState(cancelledSubscription.State);

            await _publisher.Publish(new SubscriptionStateChanged
            {
                SubscriptionId = subscriptionId,
                UserId = userId,
                MaxioSubscriptionId = subscriptionId,
                OldState = oldState,
                NewState = newState
            });

            return new SubscriptionDto
            {
                Id = cancelledSubscription.Id,
                UserId = userId,
                MaxioSubscriptionId = cancelledSubscription.Id,
                ProductId = cancelledSubscription.ProductId,
                ProductHandle = product.Handle,
                State = newState
            };
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Failed to cancel subscription: {ex.Message}", ex);
        }
    }

    public async Task<SubscriptionDto> ReactivateSubscriptionAsync(string userId, int subscriptionId)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.Default(subscriptionId, nameof(subscriptionId));

        try
        {
            var currentSubscription = await _billingClient.GetSubscriptionAsync(subscriptionId);
            var oldState = MapSubscriptionState(currentSubscription.State);

            var reactivatedSubscription = await _billingClient.ReactivateSubscriptionAsync(subscriptionId);
            var product = await _billingClient.GetProductAsync(reactivatedSubscription.ProductId);
            var newState = MapSubscriptionState(reactivatedSubscription.State);

            await _publisher.Publish(new SubscriptionStateChanged
            {
                SubscriptionId = subscriptionId,
                UserId = userId,
                MaxioSubscriptionId = subscriptionId,
                OldState = oldState,
                NewState = newState
            });

            return new SubscriptionDto
            {
                Id = reactivatedSubscription.Id,
                UserId = userId,
                MaxioSubscriptionId = reactivatedSubscription.Id,
                ProductId = reactivatedSubscription.ProductId,
                ProductHandle = product.Handle,
                State = newState
            };
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BillingProviderException($"Failed to reactivate subscription: {ex.Message}", ex);
        }
    }

    private static SubscriptionState MapSubscriptionState(string state)
    {
        return state?.ToLowerInvariant() switch
        {
            "active" => SubscriptionState.Active,
            "paused" => SubscriptionState.Paused,
            "canceled" or "cancelled" => SubscriptionState.Cancelled,
            "pending" => SubscriptionState.Pending,
            "trialing" => SubscriptionState.Trialing,
            "past_due" => SubscriptionState.PastDue,
            _ => SubscriptionState.Active
        };
    }
}

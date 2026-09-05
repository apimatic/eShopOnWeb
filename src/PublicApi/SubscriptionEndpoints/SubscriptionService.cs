using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionService
{
    private const string ReferencePrefix = "eshop:";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);
    private readonly CatalogContext _catalogContext;
    private readonly IMaxioBillingClient _maxio;
    private readonly MaxioOptions _options;

    public SubscriptionService(CatalogContext catalogContext, IMaxioBillingClient maxio, Microsoft.Extensions.Options.IOptions<MaxioOptions> options)
    {
        _catalogContext = catalogContext;
        _maxio = maxio;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        _options.Validate();
        var products = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);

        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null)
            .Select(ToPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<(SubscriptionDto Subscription, bool Created)> SubscribeAsync(
        ApplicationUser user,
        string planHandle,
        CancellationToken cancellationToken)
    {
        _options.Validate();
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new SubscriptionValidationException("A plan handle is required.");

        var userId = user.Id;
        var subscriptionReference = GetSubscriptionReference(userId);
        var userLock = UserLocks.GetOrAdd(userId, static _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var plans = await GetPlansAsync(cancellationToken);
            var selectedPlan = plans.FirstOrDefault(plan => string.Equals(plan.Handle, planHandle, StringComparison.Ordinal));
            if (selectedPlan is null)
                throw new SubscriptionValidationException("The requested subscription plan is not available.");

            var mapping = await _catalogContext.SubscriptionMappings
                .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

            if (mapping is not null)
            {
                var mappedSubscription = await _maxio.ReadSubscriptionAsync(mapping.MaxioSubscriptionId, cancellationToken);
                if (mappedSubscription is not null)
                {
                    EnsurePlanMatches(selectedPlan.Handle, mappedSubscription);
                    return (ToDto(mappedSubscription), false);
                }
            }

            // The reference lookup makes a retry safe even after the local database was lost
            // or after a previous request completed before persisting its mapping.
            var existingSubscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existingSubscription is not null)
            {
                EnsurePlanMatches(selectedPlan.Handle, existingSubscription);
                await SaveMappingAsync(userId, existingSubscription, cancellationToken);
                return (ToDto(existingSubscription), false);
            }

            var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
            if (customer is null)
            {
                try
                {
                    customer = await _maxio.CreateCustomerAsync(new MaxioCreateCustomer
                    {
                        FirstName = GetFirstName(user),
                        LastName = GetLastName(user),
                        Email = user.Email ?? user.UserName ?? userId,
                        Reference = userId
                    }, cancellationToken);
                }
                catch (MaxioApiException)
                {
                    // Maxio enforces unique customer references. If another request won
                    // the race, use the customer it created; otherwise preserve the error.
                    customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
                    if (customer is null)
                        throw;
                }
            }

            var customerSubscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            existingSubscription = customerSubscriptions.FirstOrDefault(subscription =>
                string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal));
            if (existingSubscription is not null)
            {
                EnsurePlanMatches(selectedPlan.Handle, existingSubscription);
                await SaveMappingAsync(userId, existingSubscription, cancellationToken);
                return (ToDto(existingSubscription), false);
            }

            var createdSubscription = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscription
            {
                ProductHandle = selectedPlan.Handle,
                CustomerId = customer.Id,
                Reference = subscriptionReference
            }, cancellationToken);

            await SaveMappingAsync(userId, createdSubscription, cancellationToken);
            return (ToDto(createdSubscription), true);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        _options.Validate();
        var customer = await _maxio.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDto>();

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToDto).ToArray();
    }

    private async Task SaveMappingAsync(string userId, MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        if (subscription.Customer?.Id is not int customerId || customerId == 0)
        {
            var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
            customerId = customer?.Id ?? 0;
        }

        if (customerId == 0 || subscription.Id == 0)
            return;

        var mapping = await _catalogContext.SubscriptionMappings
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        if (mapping is null)
        {
            _catalogContext.SubscriptionMappings.Add(new SubscriptionMapping(
                userId,
                customerId,
                subscription.Id,
                subscription.Product?.Handle ?? string.Empty));
        }
        else
        {
            mapping.Update(customerId, subscription.Id, subscription.Product?.Handle ?? string.Empty);
        }

        await _catalogContext.SaveChangesAsync(cancellationToken);
    }

    private static string GetSubscriptionReference(string userId) => ReferencePrefix + userId;

    private static void EnsurePlanMatches(string requestedPlanHandle, MaxioSubscription subscription)
    {
        if (!string.IsNullOrWhiteSpace(subscription.Product?.Handle) &&
            !string.Equals(subscription.Product.Handle, requestedPlanHandle, StringComparison.Ordinal))
        {
            throw new SubscriptionConflictException("The shopper already has a subscription to another plan.");
        }
    }

    private static string GetFirstName(ApplicationUser user)
    {
        var name = user.UserName ?? user.Email ?? user.Id;
        var separator = name.IndexOf('@');
        return separator > 0 ? name[..separator] : name;
    }

    private static string GetLastName(ApplicationUser user) => "eShopOnWeb";

    private static SubscriptionPlanDto ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static SubscriptionDto ToDto(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? subscription.ProductPriceInCents,
        Currency = subscription.Currency,
        State = subscription.State,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };
}

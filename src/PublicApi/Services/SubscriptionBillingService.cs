using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class SubscriptionBillingService : ISubscriptionBillingService
{
    // End-of-life states: a subscription in one of these does not block re-subscribing to the same plan.
    private static readonly HashSet<string> InactiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "ended", "trial_ended"
    };

    // Serializes subscribe calls per user so a double-click cannot create duplicates.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAppLogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioClient maxioClient,
        UserManager<ApplicationUser> userManager,
        IAppLogger<SubscriptionBillingService> logger)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsAsync(cancellationToken);
        return products.Select(MapPlan).ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            throw new InvalidOperationException($"User '{username}' was not found.");
        }

        var userLock = UserLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(user, cancellationToken);

            var existing = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var current = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
                !InactiveStates.Contains(s.State ?? string.Empty));
            if (current is not null)
            {
                _logger.LogInformation(
                    "User {Username} already has subscription {SubscriptionId} for plan {PlanHandle}; returning it instead of creating a duplicate.",
                    username, current.Id, productHandle);
                return Map(current);
            }

            var created = await _maxioClient.CreateSubscriptionAsync(
                productHandle,
                customer.Reference!,
                subscriptionReference: $"{user.Id}:{productHandle}",
                cancellationToken);
            _logger.LogInformation("Created subscription {SubscriptionId} for user {Username} on plan {PlanHandle}.",
                created.Id, username, productHandle);
            return Map(created);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            throw new InvalidOperationException($"User '{username}' was not found.");
        }

        var customer = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName ?? user.Id;
        var attributes = new MaxioCustomerAttributes
        {
            FirstName = email.Split('@')[0],
            LastName = "eShopOnWeb",
            Email = email,
            Reference = user.Id
        };

        try
        {
            var created = await _maxioClient.CreateCustomerAsync(attributes, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {Username}.", created.Id, user.UserName ?? string.Empty);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request created the customer first (reference is unique in Maxio) — re-read it.
            var customer = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
            if (customer is not null)
            {
                return customer;
            }

            throw;
        }
    }

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        Handle = product.Handle ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static SubscriptionDto Map(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        ActivatedAt = subscription.ActivatedAt,
        NextBillingAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };
}

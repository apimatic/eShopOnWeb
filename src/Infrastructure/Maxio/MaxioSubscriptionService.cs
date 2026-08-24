using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements subscription billing on top of Maxio Advanced Billing, which is the
/// system of record: the Maxio customer reference is the eShopOnWeb user id, and
/// subscriptions are read back from Maxio rather than cached locally.
/// </summary>
internal class MaxioSubscriptionService : ISubscriptionService
{
    // Subscription states that count as "already subscribed" for idempotency.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase) { "active", "trialing" };

    // Serializes subscribe attempts per user so a double-click cannot race into two subscriptions.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioClient maxioClient,
        MaxioSettings settings,
        UserManager<ApplicationUser> userManager,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _settings = settings;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products.Select(MapPlan).ToList();
    }

    public async Task<SubscriptionDetails> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(username);

        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var customer = await GetOrCreateCustomerAsync(user, cancellationToken);

        var userLock = SubscribeLocks.GetOrAdd(customer.Reference!, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var current = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
                s.State is not null && LiveStates.Contains(s.State));
            if (current is not null)
            {
                _logger.LogInformation(
                    $"User {username} already has subscription {current.Id} for plan {productHandle}; returning it instead of creating a duplicate.");
                return MapSubscription(current);
            }

            var created = await _maxioClient.CreateSubscriptionAsync(productHandle, customer.Reference!, cancellationToken);
            _logger.LogInformation($"Created Maxio subscription {created.Id} for user {username} on plan {productHandle}.");
            return MapSubscription(created);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(username);

        var customer = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<ApplicationUser> GetUserAsync(string username)
    {
        var user = await _userManager.FindByNameAsync(username);
        return user ?? throw new UserNotFoundException(username);
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var displayName = user.UserName ?? user.Email ?? user.Id;
        var firstName = displayName.Contains('@') ? displayName.Split('@')[0] : displayName;

        try
        {
            return await _maxioClient.CreateCustomerAsync(
                firstName, "Shopper", user.Email ?? displayName, user.Id, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent create (reference must be unique) — read back the winner.
            _logger.LogWarning($"Maxio customer create for reference {user.Id} returned 422; looking up the existing customer instead.");
            var winner = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static SubscriptionDetails MapSubscription(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        State = subscription.State ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? subscription.ProductPriceInCents,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        ActivatedAt = subscription.ActivatedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
    };
}

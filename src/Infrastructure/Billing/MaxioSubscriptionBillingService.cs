using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();

    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "trialing",
        "trial",
        "past_due",
        "unpaid",
        "pending",
        "assessing",
        "soft_failure",
        "paused",
        "on_hold",
        "suspended"
    };

    private readonly IMaxioApiClient _maxio;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient maxio,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();
        var products = await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(ToPlan)
            .OrderBy(p => p.Price)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        string userId,
        string email,
        string userName,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException("A productHandle is required to subscribe.", 400);
        }

        _options.EnsureConfigured();

        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BillingException($"Plan '{productHandle}' is not available.", 404);
        }

        var gate = SubscribeGates.GetOrAdd($"{userId}:{productHandle.ToLowerInvariant()}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(userId, email, userName, cancellationToken);
            var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
            if (existing != null)
            {
                _logger.LogInformation("Returning existing Maxio subscription {SubscriptionId} for user {UserId} and plan {ProductHandle}.", existing.Id, userId, productHandle);
                var mapped = ToCustomerSubscription(existing);
                mapped.Created = false;
                return mapped;
            }

            try
            {
                var created = await _maxio.CreateSubscriptionAsync(customer.Id, productHandle, cancellationToken);
                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} and plan {ProductHandle}.", created.Id, userId, productHandle);
                var mapped = ToCustomerSubscription(created);
                mapped.Created = true;
                return mapped;
            }
            catch (BillingException)
            {
                // Double-create race: another request may have succeeded; return that subscription if present.
                var raced = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
                if (raced != null)
                {
                    var mapped = ToCustomerSubscription(raced);
                    mapped.Created = false;
                    return mapped;
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();
        var customer = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Select(ToCustomerSubscription)
            .OrderByDescending(s => s.NextBillingDate)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        string userId,
        string email,
        string userName,
        CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(userName, email);
        try
        {
            var created = await _maxio.CreateCustomerAsync(new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userId
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Id, userId);
            return created;
        }
        catch (BillingException)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(userId, cancellationToken);
            if (raced != null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            IsLive(s.State) &&
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state);

    public static (string FirstName, string LastName) SplitDisplayName(string userName, string email)
    {
        var source = !string.IsNullOrWhiteSpace(email) ? email : userName;
        var local = source;
        var at = source.IndexOf('@');
        if (at > 0)
        {
            local = source[..at];
        }

        local = string.IsNullOrWhiteSpace(local) ? "Shopper" : local.Trim();
        return (local, "eShopOnWeb");
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        Price = CentsToDecimal(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        Price = CentsToDecimal(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0),
        State = subscription.State ?? string.Empty,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
    };

    private static decimal CentsToDecimal(int cents) => cents / 100m;
}

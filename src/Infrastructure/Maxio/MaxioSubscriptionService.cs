using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing as the system of record.
/// eShopOnWeb users are mapped 1:1 to Maxio customers through the Maxio customer
/// <c>reference</c> field, which holds the eShopOnWeb user id. This makes both customer
/// lookup and subscription lookup idempotent without requiring local persistence.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>Subscription states in which the customer is considered subscribed to a plan.</summary>
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "past_due", "soft_failure", "unpaid", "on_hold", "suspended"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    // Serializes ensure-customer + subscribe sequences per user+plan across the whole process,
    // so double-clicks cannot race Maxio into creating duplicate resources. Static because the
    // service is registered scoped: one instance is created per request.
    private static readonly SemaphoreSlim SubscribeLock = new(1, 1);

    public MaxioSubscriptionService(IMaxioClient maxioClient, IOptions<MaxioOptions> options, ILogger<MaxioSubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string userId, string email, string fullName, string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A user id is required to subscribe.", nameof(userId));
        }

        await SubscribeLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(userId, email, fullName, cancellationToken);
            var plan = await GetPlanAsync(planHandle, cancellationToken);

            var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = existingSubscriptions.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                LiveSubscriptionStates.Contains(s.State));

            if (existing is not null)
            {
                _logger.LogInformation(
                    "User {UserId} already holds subscription {SubscriptionId} to plan {PlanHandle}; returning it without creating a duplicate.",
                    userId, existing.Id, plan.Handle);

                return new SubscribeResult
                {
                    Created = false,
                    Subscription = MapSummary(existing)
                };
            }

            var created = await _maxioClient.CreateSubscriptionAsync(
                new MaxioCreateSubscriptionInput { ProductHandle = plan.Handle!, CustomerId = customer.Id },
                cancellationToken);

            _logger.LogInformation(
                "Subscribed user {UserId} (Maxio customer {CustomerId}) to plan {PlanHandle}: Maxio subscription {SubscriptionId}.",
                userId, customer.Id, plan.Handle, created.Id);

            return new SubscribeResult
            {
                Created = true,
                Subscription = MapSummary(created)
            };
        }
        finally
        {
            SubscribeLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsForUserAsync(string userId,
        CancellationToken cancellationToken = default)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSummary).ToList();
    }

    /// <summary>
    /// Finds the Maxio customer for the eShopOnWeb user, creating one on first use. The Maxio
    /// customer reference is unique, so a concurrent create attempt for the same user fails
    /// with 422 and is resolved by re-looking the customer up.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(string userId, string email, string fullName,
        CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(fullName, email);

        try
        {
            var created = await _maxioClient.CreateCustomerAsync(new MaxioCustomerInput
            {
                Reference = userId,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for eShopOnWeb user {UserId}.", created.Id, userId);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Lost a race with a concurrent create for the same unique reference.
            var raced = await _maxioClient.FindCustomerByReferenceAsync(userId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Maxio rejected customer creation for reference '{userId}' and no customer exists.", ex);
            return raced;
        }
    }

    private async Task<SubscriptionPlan> GetPlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var products = await _maxioClient.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        var product = products.FirstOrDefault(p =>
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase) && p.ArchivedAt is null);

        if (product is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        return MapPlan(product);
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? product.Id.ToString(),
        MaxioProductId = product.Id,
        Name = product.Name,
        Description = product.Description,
        Price = CentsToAmount(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static SubscriptionSummary MapSummary(MaxioSubscription subscription) => new()
    {
        MaxioSubscriptionId = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        Price = CentsToAmount(subscription.Product?.PriceInCents ?? 0),
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        State = subscription.State,
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
        MaxioCustomerId = subscription.Customer?.Id ?? 0
    };

    private static decimal CentsToAmount(long cents) => cents / 100m;

    private static (string FirstName, string LastName) SplitName(string fullName, string email)
    {
        var name = (fullName ?? string.Empty).Trim();
        if (name.Length > 0)
        {
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 1)
            {
                return (parts[0], "-");
            }
            return (parts[0], string.Join(' ', parts.Skip(1)));
        }

        var localPart = (email ?? "customer").Split('@')[0];
        return (localPart, "-");
    }
}

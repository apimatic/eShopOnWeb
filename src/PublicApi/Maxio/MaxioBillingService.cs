using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public record BillingUser(string UserId, string Email);

public class MaxioPlanView
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string? ProductFamilyHandle { get; init; }
}

public class MaxioSubscriptionView
{
    public int SubscriptionId { get; init; }
    public string State { get; init; } = string.Empty;
    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }
    public int? PriceInCents { get; init; }
    public string? Currency { get; init; }
    public DateTime? NextBillingDateUtc { get; init; }
    public DateTime? CreatedAtUtc { get; init; }
    public DateTime? CanceledAtUtc { get; init; }
    public int MaxioCustomerId { get; init; }
}

public class SubscribeOutcome
{
    public bool CreatedNew { get; init; }
    public int MaxioCustomerId { get; init; }
    public MaxioSubscriptionView Subscription { get; init; } = new();
}

/// <summary>
/// Per-user serialization of Maxio customer/subscription creation, so concurrent requests
/// (e.g. a double-clicked subscribe button) cannot create duplicate customers or subscriptions.
/// </summary>
public class MaxioUserLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public SemaphoreSlim GetLock(string userId) =>
        _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
}

public interface IMaxioBillingService
{
    Task<IReadOnlyList<MaxioPlanView>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeOutcome> SubscribeAsync(BillingUser user, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscriptionView>> ListSubscriptionsForUserAsync(BillingUser user, CancellationToken cancellationToken);
}

public class MaxioBillingService : IMaxioBillingService
{
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly IMaxioApiClient _apiClient;
    private readonly MaxioOptions _options;
    private readonly MaxioUserLockRegistry _lockRegistry;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        IMaxioApiClient apiClient,
        IOptions<MaxioOptions> options,
        MaxioUserLockRegistry lockRegistry,
        ILogger<MaxioBillingService> logger)
    {
        _apiClient = apiClient;
        _options = options.Value;
        _lockRegistry = lockRegistry;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioPlanView>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _apiClient.ListProductFamilyProductsAsync(_options.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .Select(p => new MaxioPlanView
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                ProductFamilyHandle = p.ProductFamily?.Handle
            })
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeOutcome> SubscribeAsync(BillingUser user, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            planHandle = _options.DefaultPlanHandle ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new PlanNotFoundException("(no plan handle supplied and no default configured)");
        }

        var userLock = _lockRegistry.GetLock(user.UserId);
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var plans = await ListPlansAsync(cancellationToken);
            var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

            if (plan is null)
            {
                throw new PlanNotFoundException(planHandle);
            }

            var customer = await EnsureCustomerCoreAsync(user, cancellationToken);

            var existingSubscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = existingSubscriptions.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                !TerminalStates.Contains(s.State));

            if (existing is not null)
            {
                _logger.LogInformation(
                    "User {UserId} is already subscribed to plan {PlanHandle} (Maxio subscription {SubscriptionId}); returning existing subscription.",
                    user.UserId, planHandle, existing.Id);

                return new SubscribeOutcome
                {
                    CreatedNew = false,
                    MaxioCustomerId = customer.Id,
                    Subscription = MapSubscription(existing, customer.Id)
                };
            }

            var created = await _apiClient.CreateSubscriptionAsync(
                new MaxioCreateSubscriptionRequest
                {
                    Subscription = new MaxioCreateSubscriptionAttributes
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customer.Id,
                        PaymentCollectionMethod = _options.PaymentCollectionMethod,
                        Reference = $"eshopweb:{user.UserId}:{plan.Handle}"
                    }
                },
                cancellationToken);

            _logger.LogInformation(
                "User {UserId} subscribed to plan {PlanHandle}; Maxio subscription {SubscriptionId} created.",
                user.UserId, plan.Handle, created.Id);

            return new SubscribeOutcome
            {
                CreatedNew = true,
                MaxioCustomerId = customer.Id,
                Subscription = MapSubscription(created, customer.Id)
            };
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscriptionView>> ListSubscriptionsForUserAsync(BillingUser user, CancellationToken cancellationToken)
    {
        var customer = await _apiClient.FindCustomerByReferenceAsync(user.UserId, cancellationToken);

        if (customer is null)
        {
            return Array.Empty<MaxioSubscriptionView>();
        }

        var subscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .Select(s => MapSubscription(s, customer.Id))
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerCoreAsync(BillingUser user, CancellationToken cancellationToken)
    {
        var existing = await _apiClient.FindCustomerByReferenceAsync(user.UserId, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(user.Email);
        var created = await _apiClient.CreateCustomerAsync(
            new MaxioCreateCustomerRequest
            {
                Customer = new MaxioCreateCustomerAttributes
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = user.Email,
                    Reference = user.UserId
                }
            },
            cancellationToken);

        _logger.LogInformation(
            "Maxio customer {CustomerId} created for user {UserId} (reference {Reference}).",
            created.Id, user.UserId, created.Reference);

        return created;
    }

    private static MaxioSubscriptionView MapSubscription(MaxioSubscription subscription, int customerId) =>
        new()
        {
            SubscriptionId = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            Currency = subscription.Currency,
            NextBillingDateUtc = subscription.CurrentPeriodEndsAt,
            CreatedAtUtc = subscription.CreatedAt,
            CanceledAtUtc = subscription.CanceledAt,
            MaxioCustomerId = customerId
        };

    private static (string FirstName, string LastName) SplitName(string email)
    {
        var local = WebUtility.UrlDecode(email ?? string.Empty);
        var atIndex = local.IndexOf('@');

        if (atIndex > 0 && atIndex < local.Length - 1)
        {
            var firstName = local[..atIndex];
            var lastName = local[(atIndex + 1)..];

            var dotIndex = firstName.IndexOf('.');
            if (dotIndex > 0 && dotIndex < firstName.Length - 1)
            {
                lastName = firstName[(dotIndex + 1)..];
                firstName = firstName[..dotIndex];
            }

            return (Truncate(Capitalize(firstName), 50), Truncate(Capitalize(lastName), 50));
        }

        return ("eShopOnWeb", "Shopper");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}

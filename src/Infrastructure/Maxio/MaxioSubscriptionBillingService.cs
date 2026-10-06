using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements the subscription-billing capability against the Maxio Advanced
/// Billing API (the billing system of record).
///
/// Idempotency model:
/// - The eShopOnWeb user id is carried as the Maxio customer "reference", which
///   Maxio enforces as unique per site. Provisioning therefore collapses to
///   lookup-or-create and can never yield two customers for one user.
/// - Before creating a subscription, live subscriptions for the customer are
///   checked and an existing one on the same plan is returned instead of
///   creating a duplicate (double-click safe). A per-user in-process lock
///   serializes concurrent signups from the same user.
/// - Subscription creation carries a uniqueness_token so a request replayed
///   within 60 minutes is rejected by Maxio with 409 instead of double-billing.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string ProductFamilyPathPrefix = "product_families/handle:";

    /// <summary>States from which the customer is considered actively subscribed to a plan.</summary>
    private static readonly HashSet<string> CanceledStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired"
    };

    private readonly MaxioApiClient _apiClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    /// <summary>Per-user locks so concurrent signups for one user are serialized.</summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    public MaxioSubscriptionBillingService(MaxioApiClient apiClient,
        IOptions<MaxioOptions> options, ILogger<MaxioSubscriptionBillingService> logger)
    {
        _apiClient = apiClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _apiClient.GetAsync<MaxioProductEnvelope[]>(
            $"{ProductFamilyPathPrefix}{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json?per_page=200",
            cancellationToken) ?? Array.Empty<MaxioProductEnvelope>();

        var plans = products
            .Where(e => e.Product is not null)
            .Select(e => e.Product)
            .Where(p => !string.IsNullOrWhiteSpace(p!.Handle))
            .Select(p => new SubscriptionPlan
            {
                Handle = p!.Handle!,
                Name = p.Name,
                Description = p.Description,
                Price = CentsToDecimal(p.PriceInCents),
                BillingInterval = p.Interval,
                BillingIntervalUnit = p.IntervalUnit ?? "month",
                RequirePaymentMethod = p.RequireCreditCard,
                Taxable = p.Taxable,
                Archived = p.ArchivedAt.HasValue
            })
            .Where(p => !p.Archived)
            .OrderBy(p => p.Price)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return plans;
    }

    public async Task<SubscriptionSignupResult> SubscribeAsync(
        BillingCustomerInfo customer, string planHandle, CancellationToken cancellationToken = default)
    {
        if (customer is null || string.IsNullOrWhiteSpace(customer.UserId))
        {
            throw new ArgumentException("A user is required to subscribe.", nameof(customer));
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required to subscribe.", nameof(planHandle));
        }

        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new MaxioPlanNotFoundException(planHandle);
        }

        var userLock = _userLocks.GetOrAdd(customer.UserId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var maxioCustomer = await EnsureCustomerAsync(customer, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(maxioCustomer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "User {UserId} already holds subscription {SubscriptionId} on plan {PlanHandle}.",
                    customer.UserId, existing.Id, plan.Handle);
                return new SubscriptionSignupResult
                {
                    Outcome = SubscriptionSignupOutcome.AlreadySubscribed,
                    Subscription = MapSubscription(existing)
                };
            }

            var request = new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscriptionAttributes
                {
                    ProductHandle = plan.Handle,
                    CustomerId = maxioCustomer.Id,
                    UniquenessToken = Guid.NewGuid().ToString("N")
                }
            };

            var created = await _apiClient.PostAsync<MaxioSubscriptionEnvelope>(
                "subscriptions.json", request, cancellationToken)
                ?? throw new MaxioApiException(
                    $"Maxio did not return the created subscription for plan '{plan.Handle}'.",
                    statusCode: 0, responseBody: null);

            _logger.LogInformation(
                "Created subscription {SubscriptionId} on plan {PlanHandle} for user {UserId} (Maxio customer {CustomerId}).",
                created.Subscription.Id, plan.Handle, customer.UserId, maxioCustomer.Id);

            return new SubscriptionSignupResult
            {
                Outcome = SubscriptionSignupOutcome.Created,
                Subscription = MapSubscription(created.Subscription)
            };
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<UserSubscription>> GetUserSubscriptionsAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A user id is required.", nameof(userId));
        }

        var maxioCustomer = await LookupCustomerAsync(userId, cancellationToken);
        if (maxioCustomer is null)
        {
            return Array.Empty<UserSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(maxioCustomer.Id, cancellationToken);
        return subscriptions
            .Select(MapSubscription)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        BillingCustomerInfo customer, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerAsync(customer.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerAttributes
            {
                FirstName = string.IsNullOrWhiteSpace(customer.FirstName) ? "eShop" : customer.FirstName,
                LastName = string.IsNullOrWhiteSpace(customer.LastName) ? "Customer" : customer.LastName,
                Email = customer.Email,
                Reference = customer.UserId
            }
        };

        try
        {
            var created = await _apiClient.PostAsync<MaxioCustomerEnvelope>(
                "customers.json", request, cancellationToken)
                ?? throw new MaxioApiException(
                    $"Maxio did not return the created customer for reference '{customer.UserId}'.",
                    statusCode: 0, responseBody: null);
            return created.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Lost a race with another request creating the same reference:
            // Maxio rejects a second customer per reference value, so the
            // customer now exists - fall back to the lookup.
            var racedCustomer = await LookupCustomerAsync(customer.UserId, cancellationToken);
            if (racedCustomer is not null)
            {
                return racedCustomer;
            }
            throw;
        }
    }

    private async Task<MaxioCustomer?> LookupCustomerAsync(string userId, CancellationToken cancellationToken)
    {
        return (await _apiClient.GetAsync<MaxioCustomerEnvelope>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(userId)}",
            cancellationToken))?.Customer;
    }

    private async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int maxioCustomerId, CancellationToken cancellationToken)
    {
        var envelopes = await _apiClient.GetAsync<MaxioSubscriptionEnvelope[]>(
            $"customers/{maxioCustomerId}/subscriptions.json?per_page=200", cancellationToken)
            ?? Array.Empty<MaxioSubscriptionEnvelope>();

        return envelopes.Where(e => e.Subscription is not null)
            .Select(e => e.Subscription!)
            .ToList();
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(
        int maxioCustomerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(maxioCustomerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            (s.State is null || !CanceledStates.Contains(s.State)));
    }

    private static UserSubscription MapSubscription(MaxioSubscription subscription)
    {
        return new UserSubscription
        {
            Id = subscription.Id,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            Price = CentsToDecimal(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0),
            State = subscription.State ?? string.Empty,
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt,
            CustomerReference = subscription.Customer?.Reference
        };
    }

    private static decimal CentsToDecimal(long cents) => cents / 100m;
}

/// <summary>
/// The requested plan does not exist (or is archived) in the configured
/// product family.
/// </summary>
public class MaxioPlanNotFoundException : Exception
{
    public MaxioPlanNotFoundException(string planHandle)
        : base($"Plan '{planHandle}' was not found in the configured product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
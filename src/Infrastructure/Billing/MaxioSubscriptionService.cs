using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// <see cref="ISubscriptionService"/> backed by Maxio Advanced Billing (system of record).
/// The eShopOnWeb user name is stored as the Maxio customer <c>reference</c>, which is how the
/// user to customer to subscriptions mapping is recovered; no local state is kept.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const string PLANS_CACHE_KEY = "maxio-plans";
    private static readonly TimeSpan PlansCacheDuration = TimeSpan.FromSeconds(60);
    private const int PAGE_SIZE = 200;

    // Subscriptions in these states no longer count as "held" by the user.
    private static readonly HashSet<string> DeadStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    // In-process serialization of subscribe calls per user (striped, bounded memory). Together with the
    // look-before-create against Maxio this makes double-clicks collapse into a single customer/subscription.
    private static readonly SemaphoreSlim[] UserLocks =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings,
        IMemoryCache cache, IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(PLANS_CACHE_KEY, out IReadOnlyList<SubscriptionPlan>? cached) && cached != null)
        {
            return cached;
        }

        var plans = await CallAsync(async () =>
        {
            // Handles are stable, numeric ids are not: resolve the family id from its handle.
            var families = await _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: cancellationToken);
            var family = families
                .Select(f => f.ProductFamily)
                .FirstOrDefault(f => string.Equals(f?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));
            if (family?.Id == null)
            {
                throw new BillingUnavailableException($"Maxio product family '{_settings.ProductFamilyHandle}' was not found.");
            }

            var products = new List<Product>();
            for (var page = 1; ; page++)
            {
                var batch = await _client.ProductFamilies.ListProductsForProductFamily(new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = family.Id.Value.ToString(),
                    Page = page,
                    PerPage = PAGE_SIZE,
                }, cancellationToken: cancellationToken);
                products.AddRange(batch.Select(b => b.Product));
                if (batch.Count < PAGE_SIZE) break;
            }

            return (IReadOnlyList<SubscriptionPlan>)products
                .Where(p => p.ArchivedAt == null && !string.IsNullOrEmpty(p.Handle))
                .Select(ToPlan)
                .OrderBy(p => p.PriceInCents)
                .ToList();
        });

        _cache.Set(PLANS_CACHE_KEY, plans, PlansCacheDuration);
        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        var plan = (await ListPlansAsync(cancellationToken))
            .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var userLock = UserLocks[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(subscriber.UserName) % UserLocks.Length];
        await userLock.WaitAsync(cancellationToken);
        try
        {
            return await CallAsync(async () =>
            {
                var customerId = await EnsureCustomerAsync(subscriber, cancellationToken);

                var existing = await GetLiveSubscriptionsAsync(customerId, cancellationToken);
                var held = existing.FirstOrDefault(s => string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));
                if (held != null)
                {
                    _logger.LogInformation($"User already subscribed to '{plan.Handle}' (subscription {held.Id}); returning existing.");
                    return new SubscribeResult(ToSubscription(held), Created: false);
                }

                SubscriptionResponse created;
                try
                {
                    created = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
                    {
                        Body = new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = plan.Handle,
                                CustomerId = customerId,
                                // No card is captured here, so bill by invoice rather than charging a payment profile.
                                PaymentCollectionMethod = CollectionMethod.Remittance,
                            }
                        }
                    }, cancellationToken: cancellationToken);
                }
                catch (ApiException<CreateSubscriptionError> ex) when ((int)ex.StatusCode == 422)
                {
                    var messages = ex.Error.TryGetErrorListResponse1(out var list) ? string.Join("; ", list.Errors) : ex.Message;
                    throw new BillingRejectedException($"Subscription was rejected by billing: {messages}", ex);
                }

                var subscription = created.Subscription
                    ?? throw new BillingUnavailableException("Billing returned an empty subscription response.");
                _logger.LogInformation($"Created Maxio subscription {subscription.Id} on '{plan.Handle}' for customer {customerId}.");
                return new SubscribeResult(ToSubscription(subscription), Created: true);
            });
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<UserSubscription>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        return await CallAsync(async () =>
        {
            var customerId = await FindCustomerIdAsync(userName, cancellationToken);
            if (customerId == null)
            {
                return (IReadOnlyList<UserSubscription>)Array.Empty<UserSubscription>();
            }

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId.Value }, cancellationToken: cancellationToken);
            return (IReadOnlyList<UserSubscription>)subscriptions
                .Select(s => s.Subscription)
                .Where(s => s != null)
                .Select(s => ToSubscription(s!))
                .ToList();
        });
    }

    private async Task<int?> FindCustomerIdAsync(string userName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = userName }, cancellationToken: cancellationToken);
            return response.Customer.Id;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<int> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existingId = await FindCustomerIdAsync(subscriber.UserName, cancellationToken);
        if (existingId != null)
        {
            return existingId.Value;
        }

        var email = subscriber.Email ?? subscriber.UserName;
        var local = email.Split('@')[0];
        try
        {
            var created = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = string.IsNullOrWhiteSpace(subscriber.FirstName) ? local : subscriber.FirstName!,
                        LastName = string.IsNullOrWhiteSpace(subscriber.LastName) ? "eShopOnWeb" : subscriber.LastName!,
                        Email = email,
                        Reference = subscriber.UserName,
                    }
                }
            }, cancellationToken: cancellationToken);
            return created.Customer.Id ?? throw new BillingUnavailableException("Billing returned a customer without an id.");
        }
        catch (ApiException<CreateCustomerError> ex) when ((int)ex.StatusCode == 422)
        {
            // Another instance may have created the customer between our lookup and create.
            var raced = await FindCustomerIdAsync(subscriber.UserName, cancellationToken);
            if (raced != null) return raced.Value;
            throw new BillingRejectedException("Billing rejected the customer details for this account.", ex);
        }
    }

    private async Task<List<Subscription>> GetLiveSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var all = await _client.Customers.ListCustomerSubscriptions(
            new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: cancellationToken);
        return all.Select(s => s.Subscription)
            .Where(s => s != null && !DeadStates.Contains(s.State?.Value ?? string.Empty))
            .Select(s => s!)
            .ToList();
    }

    /// <summary>Runs a Maxio call, translating SDK failures into application-level billing exceptions.</summary>
    private async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ApiException ex)
        {
            _logger.LogWarning($"Maxio API call failed with HTTP {(int)ex.StatusCode}: {ex.Message}");
            throw new BillingUnavailableException($"Billing system returned HTTP {(int)ex.StatusCode}.", ex);
        }
        catch (SdkException ex)
        {
            _logger.LogWarning($"Maxio SDK call failed: {ex.Message}");
            throw new BillingUnavailableException("Billing system is unreachable or timed out.", ex);
        }
    }

    private static SubscriptionPlan ToPlan(Product p) => new(
        p.Handle!,
        p.Name ?? p.Handle!,
        p.Description,
        p.PriceInCents ?? 0,
        p.Interval,
        p.IntervalUnit?.Value,
        p.TrialInterval,
        p.TrialIntervalUnit?.Value,
        p.InitialChargeInCents);

    private static UserSubscription ToSubscription(Subscription s) => new(
        s.Id?.ToString() ?? string.Empty,
        s.Product?.Handle ?? string.Empty,
        s.Product?.Name ?? s.Product?.Handle ?? string.Empty,
        s.ProductPriceInCents ?? s.Product?.PriceInCents ?? 0,
        s.State?.Value ?? "unknown",
        s.NextAssessmentAt,
        s.CurrentPeriodEndsAt,
        s.CreatedAt);
}

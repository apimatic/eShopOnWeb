using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionService"/> backed by Maxio Advanced Billing. Maxio is the system of
/// record; this service holds no local persistence. Idempotency is achieved by:
///  - using the user's e-mail as the customer <c>reference</c> (unique per site), and
///  - checking for an existing live subscription to the plan before creating a new one.
/// A per-reference in-process lock serializes concurrent subscribe calls for the same user so a
/// double-click cannot create two customers or two subscriptions on a single instance.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    // Currency is not returned on the product resource; Maxio subscriptions on this site are USD.
    private const string DefaultCurrency = "USD";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly IMaxioClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        return await GuardUpstream(async () =>
        {
            var familyId = await ResolveFamilyIdAsync(cancellationToken);
            var products = await _client.GetProductsInFamilyAsync(familyId, cancellationToken);

            return (IReadOnlyList<SubscriptionPlan>)products
                .Where(p => p.ArchivedAt is null)
                .OrderBy(p => p.PriceInCents)
                .Select(ToPlan)
                .ToList();
        });
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscriberInfo subscriber,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            var available = await SafeListPlanHandlesAsync(cancellationToken);
            throw SubscriptionBillingException.BadRequest(
                $"A plan handle is required. Available plans: {available}.");
        }

        var gate = SubscribeLocks.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await GuardUpstream(async () =>
            {
                // Validate the requested plan belongs to the configured family.
                var familyId = await ResolveFamilyIdAsync(cancellationToken);
                var products = await _client.GetProductsInFamilyAsync(familyId, cancellationToken);
                var product = products.FirstOrDefault(p =>
                    string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase) && p.ArchivedAt is null);

                if (product is null)
                {
                    var available = string.Join(", ", products.Where(p => p.ArchivedAt is null).Select(p => p.Handle));
                    throw SubscriptionBillingException.BadRequest(
                        $"Unknown plan handle '{planHandle}'. Available plans: {available}.");
                }

                var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

                // Idempotency: reuse an existing live subscription to this plan.
                var existing = await _client.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
                var live = existing
                    .Select(ToSubscription)
                    .FirstOrDefault(s =>
                        string.Equals(s.PlanHandle, product.Handle, StringComparison.OrdinalIgnoreCase) && s.IsLive);

                if (live is not null)
                {
                    _logger.LogInformation(
                        "Subscriber {Reference} already has live subscription {SubscriptionId} to plan {Plan}; reusing.",
                        subscriber.Reference, live.Id, product.Handle);
                    return new SubscribeResult(live, customer.Id, alreadyExisted: true);
                }

                var created = await _client.CreateSubscriptionAsync(
                    new SubscriptionAttributes
                    {
                        ProductHandle = product.Handle!,
                        CustomerId = customer.Id,
                        PaymentCollectionMethod = "invoice"
                    },
                    cancellationToken);

                _logger.LogInformation(
                    "Created subscription {SubscriptionId} ({State}) for subscriber {Reference} on plan {Plan}.",
                    created.Id, created.State, subscriber.Reference, product.Handle);

                return new SubscribeResult(ToSubscription(created), customer.Id, alreadyExisted: false);
            });
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberInfo subscriber,
        CancellationToken cancellationToken = default)
    {
        return await GuardUpstream(async () =>
        {
            var customer = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (customer is null)
            {
                return (IReadOnlyList<CustomerSubscription>)Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await _client.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            return subscriptions.Select(ToSubscription).ToList();
        });
    }

    // ---- helpers ----

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberInfo subscriber, CancellationToken ct)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _client.CreateCustomerAsync(
                new CustomerAttributes
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Reference = subscriber.Reference
                },
                ct);
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateReference)
        {
            // A concurrent request created the customer between our lookup and create; re-read it.
            _logger.LogInformation(
                "Customer reference {Reference} was created concurrently; re-reading existing customer.",
                subscriber.Reference);
            var raced = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, ct);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private long? _cachedFamilyId;

    private async Task<long> ResolveFamilyIdAsync(CancellationToken ct)
    {
        if (_cachedFamilyId is { } cached)
        {
            return cached;
        }

        var families = await _client.GetProductFamiliesAsync(ct);
        var family = families.FirstOrDefault(f =>
            string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family is null)
        {
            throw SubscriptionBillingException.UpstreamFailure(
                $"Configured Maxio product family '{_settings.ProductFamilyHandle}' was not found on the site.");
        }

        _cachedFamilyId = family.Id;
        return family.Id;
    }

    private async Task<string> SafeListPlanHandlesAsync(CancellationToken ct)
    {
        try
        {
            var familyId = await ResolveFamilyIdAsync(ct);
            var products = await _client.GetProductsInFamilyAsync(familyId, ct);
            return string.Join(", ", products.Where(p => p.ArchivedAt is null).Select(p => p.Handle));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list plan handles for error message.");
            return "(unavailable)";
        }
    }

    private static SubscriptionPlan ToPlan(MaxioProduct p) => new(
        handle: p.Handle ?? string.Empty,
        name: p.Name ?? p.Handle ?? string.Empty,
        description: p.Description,
        priceInCents: p.PriceInCents,
        currency: DefaultCurrency,
        interval: p.Interval,
        intervalUnit: p.IntervalUnit ?? "month");

    private static CustomerSubscription ToSubscription(MaxioSubscription s) => new(
        id: s.Id,
        state: s.State ?? "unknown",
        planHandle: s.Product.Handle ?? string.Empty,
        planName: s.Product.Name ?? s.Product.Handle ?? string.Empty,
        priceInCents: s.Product.PriceInCents,
        currency: s.Currency ?? DefaultCurrency,
        interval: s.Product.Interval,
        intervalUnit: s.Product.IntervalUnit ?? "month",
        currentPeriodStartedAt: s.CurrentPeriodStartedAt,
        currentPeriodEndsAt: s.CurrentPeriodEndsAt,
        nextBillingDate: s.NextAssessmentAt ?? s.CurrentPeriodEndsAt);

    /// <summary>
    /// Translates raw Maxio transport failures into the application-level billing exception so
    /// the API surface returns a clean 502; application validation errors pass through unchanged.
    /// </summary>
    private async Task<T> GuardUpstream<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (SubscriptionBillingException)
        {
            throw;
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Maxio API call failed.");
            throw SubscriptionBillingException.UpstreamFailure(
                "The billing system rejected the request or is unavailable. " + ex.Message, ex);
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            _logger.LogError(ex, "Could not reach the Maxio API.");
            throw SubscriptionBillingException.UpstreamFailure(
                "The billing system could not be reached.", ex);
        }
    }
}

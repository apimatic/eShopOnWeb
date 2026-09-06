using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing.
/// <para>
/// Maxio is the system of record: eShopOnWeb persists no copy of the customer or subscription, and
/// every read goes back to Maxio. The link between the two systems is the Maxio customer
/// <c>reference</c>, derived from the eShopOnWeb user name.
/// </para>
/// <para>
/// Subscribing is idempotent at three levels, so a double-clicked Subscribe button can never produce
/// two customers or two subscriptions:
/// <list type="number">
/// <item>concurrent calls for one shopper are serialised in-process;</item>
/// <item>an existing live subscription to the same plan is returned as-is instead of creating another;</item>
/// <item>the create carries a deterministic <c>uniqueness_token</c>, so even a request that crossed
/// process or instance boundaries is rejected by Maxio rather than duplicated - and that rejection is
/// recovered by re-reading the subscription that did get created.</item>
/// </list>
/// </para>
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string PlanCacheKey = "Maxio:Plans";
    private const string PaymentCollectionMethodCacheKey = "Maxio:PaymentCollectionMethod";

    /// <summary>
    /// Striped locks that serialise concurrent subscribe calls for the same shopper. Striping keeps
    /// the set of locks bounded (an unbounded per-key dictionary would leak); an occasional collision
    /// only means two unrelated shoppers subscribe one after the other.
    /// </summary>
    private static readonly SemaphoreSlim[] SubscribeLocks =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static readonly MaxioSettingsValidator SettingsValidator = new();

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (_settings.PlanCacheSeconds <= 0)
        {
            return await LoadPlansAsync(cancellationToken);
        }

        if (_cache.TryGetValue(PlanCacheKey, out IReadOnlyCollection<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var plans = await LoadPlansAsync(cancellationToken);
        _cache.Set(PlanCacheKey, plans, TimeSpan.FromSeconds(_settings.PlanCacheSeconds));
        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var plan = await ResolvePlanAsync(request.PlanHandle, cancellationToken);
        var customerReference = MaxioCustomerReference.ForUser(_settings.CustomerReferencePrefix, request.Subscriber.UserName);

        var gate = LockFor(customerReference);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(request.Subscriber, customerReference, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Maxio customer {CustomerId} is already subscribed to plan {PlanHandle} (subscription {SubscriptionId}, state {State}); returning it unchanged.",
                    customer.Id, plan.Handle, existing.Id, existing.State);
                return SubscribeResult.AlreadyExisted(ToCustomerSubscription(existing, customerReference));
            }

            var createRequest = new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscriptionAttributes
                {
                    CustomerId = customer.Id,
                    ProductHandle = plan.Handle,
                    PaymentCollectionMethod = await ResolvePaymentCollectionMethodAsync(cancellationToken)
                },
                UniquenessToken = MaxioCustomerReference.UniquenessToken(
                    customerReference,
                    plan.Handle,
                    request.IdempotencyKey,
                    _settings.IdempotencyWindowSeconds,
                    DateTimeOffset.UtcNow)
            };

            try
            {
                var created = await _client.CreateSubscriptionAsync(createRequest, cancellationToken);
                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle} (state {State}).",
                    created.Id, customer.Id, plan.Handle, created.State);
                return SubscribeResult.NewlyCreated(ToCustomerSubscription(created, customerReference));
            }
            catch (MaxioApiException ex) when (ex.IsDuplicateSubmission)
            {
                // Maxio already accepted an identical create. Recover the subscription it produced
                // rather than failing a shopper whose first request simply lost its response.
                _logger.LogWarning(
                    "Maxio rejected a duplicate subscribe for customer {CustomerId} on plan {PlanHandle}; re-reading the subscription it already created.",
                    customer.Id, plan.Handle);

                var recovered = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
                if (recovered is not null)
                {
                    return SubscribeResult.AlreadyExisted(ToCustomerSubscription(recovered, customerReference));
                }

                // No subscription came out of it, so the token was burned by an attempt that failed.
                // Retrying with a fresh token here would defeat the cross-instance duplicate guard,
                // so the caller is told to re-read and retry once the window has moved on.
                throw new BillingConflictException(
                    "A recent subscribe attempt for this plan is still being de-duplicated by the billing system. " +
                    "Check GET /api/my-subscriptions; if nothing appears there, retry in a minute.");
            }
        }
        catch (MaxioApiException ex)
        {
            throw MaxioErrorTranslator.Translate(ex);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        EnsureConfigured();

        var customerReference = MaxioCustomerReference.ForUser(_settings.CustomerReferencePrefix, subscriber.UserName);

        try
        {
            var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                // The shopper has never subscribed, so no customer exists yet. That is not an error.
                return Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

            return subscriptions
                .Select(s => ToCustomerSubscription(s, customerReference))
                .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
                .ToList();
        }
        catch (MaxioApiException ex)
        {
            throw MaxioErrorTranslator.Translate(ex);
        }
    }

    /// <summary>
    /// Rejects a call with an actionable message when the "Maxio" section is incomplete, instead of
    /// letting it fail as an opaque 401 or DNS error against a half-built base address.
    /// </summary>
    private void EnsureConfigured()
    {
        var result = SettingsValidator.Validate(name: null, _settings);
        if (!result.Failed)
        {
            return;
        }

        throw new BillingConfigurationException(
            "Subscription billing is not configured. " + string.Join(" ", result.Failures ?? Array.Empty<string>()));
    }

    private async Task<IReadOnlyCollection<SubscriptionPlan>> LoadPlansAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MaxioProduct> products;
        try
        {
            products = await _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            throw MaxioErrorTranslator.Translate(ex);
        }

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle!,
                Name = p.Name ?? p.Handle!,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty,
                RequiresPaymentMethod = p.RequireCreditCard,
                TrialInterval = p.TrialInterval,
                TrialIntervalUnit = p.TrialIntervalUnit,
                ProductFamilyHandle = p.ProductFamily?.Handle ?? _settings.ProductFamilyHandle
            })
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Handle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Decides how renewals are collected for the subscriptions this app creates.
    /// <para>
    /// eShopOnWeb never captures a payment method, so an automatically-collected subscription cannot
    /// settle its signup charge and Maxio refuses the signup. Invoice-based collection is therefore
    /// the default, and the correct spelling of it depends on the site's architecture: Relationship
    /// Invoicing sites accept "remittance", statement-based sites accept "invoice". The site is read
    /// once and cached. A deployment that does capture cards can override this with
    /// "Maxio:PaymentCollectionMethod".
    /// </para>
    /// </summary>
    private async Task<string> ResolvePaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod))
        {
            return _settings.PaymentCollectionMethod!.Trim();
        }

        if (_cache.TryGetValue(PaymentCollectionMethodCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        var site = await _client.ReadSiteAsync(cancellationToken);
        var method = site?.RelationshipInvoicingEnabled == true ? "remittance" : "invoice";

        _logger.LogInformation(
            "Maxio site {SiteName} uses {Architecture}; new subscriptions will collect payment by {PaymentCollectionMethod}.",
            site?.Name ?? "(unknown)",
            site?.RelationshipInvoicingEnabled == true ? "Relationship Invoicing" : "statement-based billing",
            method);

        if (_settings.SiteCacheSeconds > 0)
        {
            _cache.Set(PaymentCollectionMethodCacheKey, method, TimeSpan.FromSeconds(_settings.SiteCacheSeconds));
        }

        return method;
    }

    /// <summary>
    /// Resolves the requested plan against the configured catalog. Subscribing is deliberately
    /// restricted to plans in the configured product family, so a caller cannot enroll onto an
    /// arbitrary product that happens to exist on the Maxio site.
    /// </summary>
    private async Task<SubscriptionPlan> ResolvePlanAsync(string? requestedHandle, CancellationToken cancellationToken)
    {
        var handle = requestedHandle ?? _settings.DefaultPlanHandle;
        var plans = await GetPlansAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(handle))
        {
            // Missing input rather than a missing plan, so this is a bad request, not a 404.
            throw new BillingValidationException(
                $"No plan was specified and no default is configured. Specify planHandle as one of: {DescribeAvailablePlans(plans)}.");
        }

        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new BillingPlanNotFoundException(
                $"Plan '{handle}' is not available. Available plans: {DescribeAvailablePlans(plans)}.");
        }

        return plan;
    }

    private static string DescribeAvailablePlans(IReadOnlyCollection<SubscriptionPlan> plans) =>
        plans.Count == 0 ? "(none)" : string.Join(", ", plans.Select(p => p.Handle));

    /// <summary>
    /// Returns the Maxio customer for this shopper, creating it on first use. If a concurrent caller
    /// wins the create, Maxio rejects ours on the unique reference and we adopt theirs.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, string customerReference, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var (firstName, lastName) = SplitName(subscriber);

            try
            {
                var created = await _client.CreateCustomerAsync(
                    new MaxioCreateCustomerRequest
                    {
                        Customer = new MaxioCreateCustomerAttributes
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = subscriber.Email,
                            Reference = customerReference
                        },
                        UniquenessToken = MaxioCustomerReference.UniquenessToken(
                            customerReference,
                            "customer",
                            idempotencyKey: null,
                            _settings.IdempotencyWindowSeconds,
                            DateTimeOffset.UtcNow)
                    },
                    cancellationToken);

                _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.", created.Id, customerReference);
                return created;
            }
            catch (MaxioApiException ex) when (ex.IsDuplicateSubmission || ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                // Either our own replay or a racing request already created it; the unique reference
                // is what makes this recoverable.
                var raced = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
                if (raced is not null)
                {
                    return raced;
                }

                throw;
            }
        }
        catch (MaxioApiException ex)
        {
            throw MaxioErrorTranslator.Translate(ex);
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions
            .Where(s => SubscriptionStates.IsLive(s.State))
            .Where(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    /// <summary>
    /// Maxio requires a first and last name. eShopOnWeb accounts only carry an email address, so a
    /// best-effort name is derived from its local part.
    /// </summary>
    private static (string FirstName, string LastName) SplitName(SubscriberIdentity subscriber)
    {
        if (!string.IsNullOrWhiteSpace(subscriber.FirstName) || !string.IsNullOrWhiteSpace(subscriber.LastName))
        {
            return (Fallback(subscriber.FirstName, "eShopOnWeb"), Fallback(subscriber.LastName, "Shopper"));
        }

        var localPart = subscriber.Email.Split('@')[0];
        var words = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var first = words.Length > 0 ? Capitalize(words[0]) : "eShopOnWeb";
        var last = words.Length > 1 ? Capitalize(string.Join(' ', words.Skip(1))) : "Shopper";
        return (first, last);

        static string Capitalize(string value) =>
            value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string Fallback(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim();

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription, string customerReference) => new()
    {
        Id = subscription.Id,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference ?? customerReference,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        State = subscription.State ?? "unknown",
        PriceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt
    };

    private static SemaphoreSlim LockFor(string customerReference)
    {
        var hash = (uint)StringComparer.Ordinal.GetHashCode(customerReference);
        return SubscribeLocks[hash % (uint)SubscribeLocks.Length];
    }
}

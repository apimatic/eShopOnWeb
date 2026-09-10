using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionService"/> backed by Maxio Advanced Billing. Owns the orchestration
/// that the raw <see cref="IMaxioApiClient"/> does not: resolving the configured product family,
/// mapping to domain types, translating billing errors, and — critically — idempotency so that a
/// double-clicked subscribe never creates duplicate customers or subscriptions.
/// </summary>
internal sealed class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>
    /// Demo fallback plan used only when the caller omits a plan handle. It is not a
    /// configuration value: the handle is always validated against the live product family, so
    /// on a different catalog an omitted handle simply yields a 400 listing the real plans.
    /// </summary>
    private const string DefaultPlanHandle = "eshop-pro";

    /// <summary>
    /// These plans require no payment method; the default "automatic" collection would try to
    /// charge immediately and fail with "No payment method was on file". "remittance" (invoice)
    /// lets a card-free subscription activate. Verified against the sandbox.
    /// </summary>
    private const string PaymentCollectionMethod = "remittance";

    // Subscription states that mean the customer is NOT currently enrolled, so a fresh
    // subscribe should proceed rather than return the old record.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "trial_ended", "failed_to_create"
    };

    private readonly IMaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    // Serializes concurrent subscribe/customer operations for the same user so a double-click
    // cannot race two customer-creates or two subscription-creates. This coordinates across the
    // per-request (scoped) service instances, so it must be shared process-wide.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ReferenceLocks = new();

    // Per-instance (per-request) memoisation so a single request never re-fetches the family or
    // its plans. Kept instance-scoped deliberately: it avoids shared mutable state (simpler,
    // testable) and the re-fetch cost across requests is a single cheap GET, well within limits.
    private int? _cachedFamilyId;
    private IReadOnlyList<SubscriptionPlan>? _cachedPlans;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
        _options.Validate();
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        return await GetPlansCachedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubscriptionDetails> SubscribeAsync(
        SubscriberIdentity subscriber, string? planHandle, CancellationToken cancellationToken = default)
    {
        if (subscriber is null || string.IsNullOrWhiteSpace(subscriber.Reference))
        {
            throw new ArgumentException("A subscriber with a non-empty reference is required.", nameof(subscriber));
        }

        var plans = await GetPlansCachedAsync(cancellationToken).ConfigureAwait(false);
        var effectiveHandle = string.IsNullOrWhiteSpace(planHandle) ? DefaultPlanHandle : planHandle.Trim();

        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, effectiveHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new UnknownPlanException(planHandle, plans.Select(p => p.Handle));
        }

        var gate = ReferenceLocks.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken).ConfigureAwait(false);

            // Idempotency: if the customer already has a non-terminal subscription to this plan, return it.
            var existing = await ExecuteAsync(
                () => _client.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken),
                "list customer subscriptions").ConfigureAwait(false);

            var current = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                !IsTerminal(s.State));

            if (current is not null)
            {
                _logger.LogInformation(
                    "Subscribe is idempotent: customer {0} already has subscription {1} to plan {2} (state {3}).",
                    customer.Id, current.Id, plan.Handle, current.State ?? "?");
                return MapSubscription(current, alreadyExisted: true);
            }

            var created = await ExecuteAsync(
                () => _client.CreateSubscriptionAsync(new SubscriptionInput
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    PaymentCollectionMethod = PaymentCollectionMethod
                }, cancellationToken),
                "create subscription").ConfigureAwait(false);

            _logger.LogInformation(
                "Created subscription {0} for customer {1} on plan {2} (state {3}).",
                created.Id, customer.Id, plan.Handle, created.State ?? "?");

            return MapSubscription(created, alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        if (subscriber is null || string.IsNullOrWhiteSpace(subscriber.Reference))
        {
            throw new ArgumentException("A subscriber with a non-empty reference is required.", nameof(subscriber));
        }

        var customer = await ExecuteAsync(
            () => _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken),
            "look up customer").ConfigureAwait(false);

        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await ExecuteAsync(
            () => _client.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken),
            "list customer subscriptions").ConfigureAwait(false);

        return subscriptions.Select(s => MapSubscription(s, alreadyExisted: true)).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await ExecuteAsync(
            () => _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken),
            "look up customer").ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ResolveName(subscriber);
        var created = await ExecuteAsync(
            () => _client.CreateCustomerAsync(new CustomerInput
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference
            }, cancellationToken),
            "create customer").ConfigureAwait(false);

        _logger.LogInformation("Created Maxio customer {0} for reference {1}.", created.Id, subscriber.Reference);
        return created;
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> GetPlansCachedAsync(CancellationToken cancellationToken)
    {
        if (_cachedPlans is not null)
        {
            return _cachedPlans;
        }

        var familyId = await ResolveFamilyIdAsync(cancellationToken).ConfigureAwait(false);
        var products = await ExecuteAsync(
            () => _client.GetProductsByFamilyAsync(familyId, cancellationToken),
            "list products").ConfigureAwait(false);

        _cachedPlans = products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();

        return _cachedPlans;
    }

    private async Task<int> ResolveFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_cachedFamilyId is { } cached)
        {
            return cached;
        }

        var families = await ExecuteAsync(
            () => _client.GetProductFamiliesAsync(cancellationToken),
            "list product families").ConfigureAwait(false);

        var family = families.FirstOrDefault(f =>
            string.Equals(f.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family is null)
        {
            throw new SubscriptionBillingException(
                $"Configured product family '{_options.ProductFamilyHandle}' was not found on the Maxio site.");
        }

        _cachedFamilyId = family.Id;
        return family.Id;
    }

    private static bool IsTerminal(string? state) =>
        state is not null && TerminalStates.Contains(state);

    private static (string FirstName, string LastName) ResolveName(SubscriberIdentity subscriber)
    {
        if (!string.IsNullOrWhiteSpace(subscriber.FirstName) || !string.IsNullOrWhiteSpace(subscriber.LastName))
        {
            return (subscriber.FirstName?.Trim() ?? string.Empty, subscriber.LastName?.Trim() ?? string.Empty);
        }

        // Derive a display name from the email local-part when no name is available.
        var local = subscriber.Email.Contains('@') ? subscriber.Email[..subscriber.Email.IndexOf('@')] : subscriber.Email;
        return (local, "(eShopOnWeb)");
    }

    private static SubscriptionPlan MapPlan(MaxioProduct p) => new(
        ProductId: p.Id,
        Handle: p.Handle!,
        Name: p.Name ?? p.Handle!,
        Description: p.Description,
        Price: CentsToDecimal(p.PriceInCents),
        Interval: p.Interval,
        IntervalUnit: p.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod: p.RequireCreditCard);

    private static SubscriptionDetails MapSubscription(MaxioSubscription s, bool alreadyExisted) => new(
        Id: s.Id,
        State: s.State ?? "unknown",
        PlanHandle: s.Product?.Handle ?? string.Empty,
        PlanName: s.Product?.Name ?? string.Empty,
        Price: CentsToDecimal(s.ProductPriceInCents ?? s.Product?.PriceInCents),
        Currency: s.Currency ?? "USD",
        Interval: s.Product?.Interval ?? 0,
        IntervalUnit: s.Product?.IntervalUnit ?? string.Empty,
        CurrentPeriodStartsAt: s.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt: s.CurrentPeriodEndsAt,
        NextBillingDate: s.NextAssessmentAt,
        CreatedAt: s.CreatedAt,
        CustomerId: s.Customer?.Id ?? 0,
        CustomerReference: s.Customer?.Reference,
        PaymentCollectionMethod: s.PaymentCollectionMethod ?? string.Empty)
    {
        AlreadyExisted = alreadyExisted
    };

    private static decimal CentsToDecimal(long? cents) => (cents ?? 0) / 100m;

    /// <summary>Runs a Maxio call, translating transport/API failures into a domain <see cref="SubscriptionBillingException"/>.</summary>
    private async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string operation)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogWarning("Maxio call to {0} failed: {1}", operation, ex.Message);
            throw new SubscriptionBillingException($"The billing system rejected the attempt to {operation}: {string.Join("; ", ex.Errors)}", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Maxio call to {0} could not reach the billing system: {1}", operation, ex.Message);
            throw new SubscriptionBillingException($"Could not reach the billing system to {operation}.", ex);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.CancellationToken == default)
        {
            _logger.LogWarning("Maxio call to {0} timed out.", operation);
            throw new SubscriptionBillingException($"The billing system timed out while trying to {operation}.", ex);
        }
    }
}

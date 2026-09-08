using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio.Dtos;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <inheritdoc cref="ISubscriptionService"/>
public sealed class SubscriptionService : ISubscriptionService
{
    // Subscription states that represent a live/billable relationship. Subscriptions in these
    // states are returned as-is when the same plan is requested again so a double-click can never
    // create two subscriptions. Terminal states (canceled, expired, ...) allow a fresh signup.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "past_due", "soft_failure",
        "suspended", "paused", "unpaid", "on_hold", "awaiting_signup"
    };

    // Serializes the ensure-customer / find-or-create-subscription critical section per shopper
    // (keyed by customer reference) so concurrent double-clicks cannot both create.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PerCustomerLocks = new();

    private readonly IMaxioBillingApi _api;
    private readonly MaxioBillingOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioBillingApi api,
        IOptions<MaxioBillingOptions> options,
        IMemoryCache cache,
        ILogger<SubscriptionService> logger)
    {
        _api = api;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct)
    {
        var catalog = await LoadCatalogAsync(ct);
        return catalog.Plans;
    }

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(SubscriptionEnrollmentInput input, CancellationToken ct)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        // Validate the requested plan before mutating anything.
        var catalog = await LoadCatalogAsync(ct);
        var plan = catalog.Plans.FirstOrDefault(p =>
            string.Equals(p.Handle, input.PlanHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(input.PlanHandle);
        }

        var gate = PerCustomerLocks.GetOrAdd(input.CustomerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var customer = await EnsureCustomerAsync(input, ct);

            var existingSubscriptions = await _api.ListCustomerSubscriptionsAsync(customer.Id, ct);
            var existing = existingSubscriptions
                .Select(e => e.Subscription)
                .Where(s => s is not null)
                .FirstOrDefault(s =>
                    LiveStates.Contains(s!.State ?? string.Empty) &&
                    string.Equals(s.Product?.Handle, input.PlanHandle, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Shopper '{Reference}' already has an active '{PlanHandle}' subscription ({SubscriptionId}); returning it.",
                    input.CustomerReference, input.PlanHandle, existing.Id);

                return new SubscriptionEnrollmentResult
                {
                    CreatedNow = false,
                    Subscription = MapSubscription(existing)
                };
            }

            var created = await CreateSubscriptionAsync(input.CustomerReference, plan, ct);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for shopper '{Reference}' on plan '{PlanHandle}'.",
                created.Id, input.CustomerReference, plan.Handle);

            return new SubscriptionEnrollmentResult
            {
                CreatedNow = true,
                Subscription = MapSubscription(created)
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string customerReference, CancellationToken ct)
    {
        var customer = await _api.FindCustomerByReferenceAsync(customerReference, ct);
        if (customer?.Customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _api.ListCustomerSubscriptionsAsync(customer.Customer.Id, ct);
        return subscriptions
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriptionEnrollmentInput input, CancellationToken ct)
    {
        var existing = await _api.FindCustomerByReferenceAsync(input.CustomerReference, ct);
        if (existing?.Customer is { } found)
        {
            return found;
        }

        var (firstName, lastName) = ResolveCustomerName(input);

        try
        {
            var created = await _api.CreateCustomerAsync(new MaxioCreateCustomerEnvelope
            {
                Customer = new MaxioCreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = input.Email,
                    Reference = input.CustomerReference
                }
            }, ct);

            _logger.LogInformation("Created Maxio customer {CustomerId} for shopper reference '{Reference}'.",
                created.Customer?.Id, input.CustomerReference);
            return created.Customer ?? throw new MaxioApiException(
                HttpStatusCode.OK, "customers.json", new[] { "Customer creation returned no customer." });
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference uniqueness is enforced by Maxio. If we lose a create/create race the
            // loser lands here; the winner's customer is authoritative — just reuse it.
            var raced = await _api.FindCustomerByReferenceAsync(input.CustomerReference, ct);
            if (raced?.Customer is not { } winner)
            {
                throw;
            }

            _logger.LogInformation("Maxio customer for reference '{Reference}' was created concurrently ({CustomerId}); reusing it.",
                input.CustomerReference, winner.Id);
            return winner;
        }
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, SubscriptionPlanDto plan, CancellationToken ct)
    {
        var created = await _api.CreateSubscriptionAsync(new MaxioCreateSubscriptionEnvelope
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerReference = customerReference,
                // Plans that do not require a stored payment method are billed on (remittance)
                // invoices so subscribing works without card capture. Plans that require a card
                // keep the site default, which will demand a payment profile.
                PaymentCollectionMethod = plan.RequiresCreditCard ? null : "remittance"
            }
        }, ct);

        return created.Subscription ?? throw new MaxioApiException(
            HttpStatusCode.Created, "subscriptions.json", new[] { "Subscription creation returned no subscription." });
    }

    private async Task<PlansCatalog> LoadCatalogAsync(CancellationToken ct)
    {
        string familyHandle = _options.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: a product family handle (Maxio:ProductFamilyHandle / " +
                "MAXIO_DEFAULT_PRODUCT_FAMILY) is required to browse subscription plans.");
        }

        string cacheKey = $"maxio:plans:{familyHandle}";
        if (_cache.TryGetValue(cacheKey, out PlansCatalog? cached) && cached is not null)
        {
            return cached;
        }

        var families = await _api.ListProductFamiliesAsync(ct);
        var family = families
            .Select(e => e.ProductFamily)
            .FirstOrDefault(f => string.Equals(f?.Handle, familyHandle, StringComparison.OrdinalIgnoreCase));

        if (family is null)
        {
            var available = string.Join(", ", families
                .Select(e => e.ProductFamily?.Handle)
                .Where(h => !string.IsNullOrWhiteSpace(h)));
            throw new MaxioConfigurationException(
                $"Maxio product family '{familyHandle}' was not found on site " +
                $"'{_options.Subdomain}'. Available families: {(available.Length > 0 ? available : "(none)")}.");
        }

        var products = await _api.ListProductsAsync(family.Id, ct);
        var plans = products
            .Select(e => e.Product)
            .Where(p => p is not null)
            .Where(p => string.IsNullOrWhiteSpace(p!.ArchivedAt)) // only current, non-archived plans
            .Select(p => MapPlan(p!, family.Handle ?? familyHandle))
            .OrderBy(p => p.PriceInCents)
            .ToList();

        var catalog = new PlansCatalog(family.Id, plans);
        _cache.Set(cacheKey, catalog, TimeSpan.FromMinutes(5));
        return catalog;
    }

    private static SubscriptionPlanDto MapPlan(MaxioProduct product, string familyHandle) => new()
    {
        ProductId = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        BillingInterval = product.Interval,
        BillingIntervalUnit = product.IntervalUnit ?? string.Empty,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        Taxable = product.Taxable ?? false,
        RequiresCreditCard = product.RequireCreditCard ?? false,
        ProductFamilyHandle = familyHandle
    };

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription)
    {
        string state = subscription.State ?? string.Empty;
        string planHandle = subscription.Product?.Handle ?? string.Empty;
        string planName = subscription.Product?.Name ?? string.Empty;
        long priceInCents = subscription.ProductPriceInCents > 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0;

        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = state,
            IsActive = LiveStates.Contains(state),
            PlanHandle = planHandle,
            PlanName = planName,
            PriceInCents = priceInCents,
            Currency = subscription.Currency,
            NextBillingDate = MaxioDateTime.TryParse(subscription.CurrentPeriodEndsAt)
                               ?? MaxioDateTime.TryParse(subscription.NextAssessmentAt),
            CurrentPeriodEndsAt = MaxioDateTime.TryParse(subscription.CurrentPeriodEndsAt),
            CurrentPeriodStartedAt = MaxioDateTime.TryParse(subscription.CurrentPeriodStartedAt),
            ActivatedAt = MaxioDateTime.TryParse(subscription.ActivatedAt),
            CanceledAt = MaxioDateTime.TryParse(subscription.CanceledAt),
            CreatedAt = MaxioDateTime.TryParse(subscription.CreatedAt)?.UtcDateTime ?? DateTimeOffset.UtcNow,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod
        };
    }

    /// <summary>
    /// Billing name for the Maxio customer. Prefers the caller-provided name; otherwise derives a
    /// deterministic name from the email local part (Maxio requires first and last name on a customer).
    /// </summary>
    private static (string FirstName, string LastName) ResolveCustomerName(SubscriptionEnrollmentInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.FirstName) && !string.IsNullOrWhiteSpace(input.LastName))
        {
            return (input.FirstName.Trim(), input.LastName.Trim());
        }

        string local = (input.Email ?? string.Empty).Split('@')[0].Trim();
        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        string first = parts.Length > 0 ? parts[0] : "Shopper";
        string last = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "Member";
        return (ToTitleCase(first), ToTitleCase(last));
    }

    private static string ToTitleCase(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private sealed record PlansCatalog(long FamilyId, IReadOnlyList<SubscriptionPlanDto> Plans);
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Raised when the requested plan handle is not a subscribable plan in the configured product family.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"The plan '{planHandle}' is not available for subscription.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>The outcome of an idempotent subscribe attempt.</summary>
public sealed class SubscriptionEnrollmentResult
{
    public SubscriptionEnrollmentResult(bool created, SubscriptionDto subscription)
    {
        Created = created;
        Subscription = subscription;
    }

    /// <summary>True when a new subscription was created; false when an existing one was returned.</summary>
    public bool Created { get; }

    public SubscriptionDto Subscription { get; }
}

/// <summary>
/// Orchestrates the subscription capability against Maxio Advanced Billing (the billing system of
/// record). A Maxio customer is keyed by the eShopOnWeb user's email address through Maxio's
/// customer <c>reference</c> field, so a user always maps to exactly one Maxio customer and no
/// local (database) copy of the mapping is required.
/// </summary>
public class SubscriptionService
{
    private const string PaymentCollectionMethodRemittance = "remittance";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PerUserLocks = new();

    // States that represent an active, billable subscription. Anything else (canceled, expired,
    // failed_to_create, trial_ended, ...) is treated as no longer "live" so the user may subscribe afresh.
    private static readonly HashSet<string> ClosedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly IMaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(IMaxioApiClient client, MaxioOptions options, ILogger<SubscriptionService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    /// <summary>Lists the subscribable plans from the configured Maxio product family.</summary>
    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        _options.EnsureConfigured();

        var products = await _client.ListProductsAsync(cancellationToken);
        var plans = products
            .Where(p => p.ArchivedAt is null && MatchesConfiguredFamily(p))
            .Select(ToPlanDto)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation("Listed {PlanCount} subscribable plans from Maxio family '{Family}'.",
            plans.Count, _options.ProductFamilyHandle);
        return plans;
    }

    /// <summary>
    /// Subscribes the identified user to the given plan. Idempotent: a user who already holds a live
    /// subscription to the plan receives that subscription back instead of a second one, and a
    /// double-click (concurrent) request is serialized per user.
    /// </summary>
    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(string userReference, string planHandle, CancellationToken cancellationToken)
    {
        _options.EnsureConfigured();

        var plan = await GetSubscribablePlanAsync(planHandle, cancellationToken)
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        string reference = NormalizeUserReference(userReference);
        var perUserLock = PerUserLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await perUserLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await GetOrCreateCustomerAsync(reference, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle!, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "User '{Reference}' already has live subscription {SubscriptionId} to plan '{PlanHandle}'; returning it.",
                    reference, existing.Id, plan.Handle);
                return new SubscriptionEnrollmentResult(false, ToSubscriptionDto(existing));
            }

            MaxioSubscription created = await CreateSubscriptionSafelyAsync(reference, plan.Handle!, cancellationToken);
            _logger.LogInformation(
                "User '{Reference}' subscribed to plan '{PlanHandle}'; Maxio subscription {SubscriptionId} created (state {State}).",
                reference, plan.Handle, created.Id, created.State);

            return new SubscriptionEnrollmentResult(true, ToSubscriptionDto(created));
        }
        finally
        {
            perUserLock.Release();
        }
    }

    /// <summary>Lists the caller's subscriptions (empty when the user has no Maxio customer yet).</summary>
    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userReference, CancellationToken cancellationToken)
    {
        _options.EnsureConfigured();

        string reference = NormalizeUserReference(userReference);
        var customer = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(ToSubscriptionDto)
            .ToList();
    }

    private async Task<MaxioProduct?> GetSubscribablePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var product = await _client.GetProductByHandleAsync(planHandle.Trim(), cancellationToken);
        if (product is null || product.ArchivedAt is not null || !MatchesConfiguredFamily(product))
        {
            return null;
        }

        return product;
    }

    private bool MatchesConfiguredFamily(MaxioProduct product)
    {
        return string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions
            .Where(s => !string.IsNullOrEmpty(s.State) &&
                        !ClosedStates.Contains(s.State) &&
                        string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var customer = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = DeriveDisplayName(reference);
        var attributes = new CustomerAttributes
        {
            FirstName = firstName,
            LastName = lastName,
            Email = reference,
            Reference = reference
        };

        _logger.LogInformation("No Maxio customer for '{Reference}'; creating one.", reference);
        try
        {
            return await _client.CreateCustomerAsync(attributes, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Another concurrent request (or a retried one) may have created the customer between our
            // lookup and create. Maxio only allows one customer per reference, so reconcile by lookup.
            customer = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (customer is not null)
            {
                return customer;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription> CreateSubscriptionSafelyAsync(string reference, string planHandle, CancellationToken cancellationToken)
    {
        var attributes = new SubscriptionAttributes
        {
            CustomerReference = reference,
            ProductHandle = planHandle,
            // Enrolls the customer without attempting to collect a card payment (the plans do not
            // require a payment method); Maxio bills by invoice and the subscription becomes active.
            PaymentCollectionMethod = PaymentCollectionMethodRemittance
        };

        // Deterministic token scoped to (user, plan): Maxio rejects a second submission of the same
        // token within an hour, which turns a double-click/retry into a 409 instead of a duplicate.
        string uniquenessToken = $"eshop-subscribe:{reference}:{planHandle}";
        try
        {
            return await _client.CreateSubscriptionAsync(attributes, uniquenessToken, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 409)
        {
            // Duplicate submission: the earlier request either already created the subscription (most
            // likely) or was a stale token from an attempt that did not leave a live subscription
            // behind. Reconcile against Maxio before doing anything else.
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                var customer = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
                if (customer is not null)
                {
                    var live = await FindLiveSubscriptionAsync(customer.Id, planHandle, cancellationToken);
                    if (live is not null)
                    {
                        _logger.LogInformation(
                            "Create-subscription for '{Reference}' plan '{PlanHandle}' reported duplicate; reconciled to live subscription {SubscriptionId}.",
                            reference, planHandle, live.Id);
                        return live;
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken);
            }

            // The duplicate token was stale (e.g. an earlier request for the same plan that was later
            // canceled). Retry once with a fresh token so a genuine re-subscribe still succeeds.
            _logger.LogInformation(
                "Create-subscription for '{Reference}' plan '{PlanHandle}' hit a stale duplicate token; retrying with a fresh token.",
                reference, planHandle);
            return await _client.CreateSubscriptionAsync(attributes, Guid.NewGuid().ToString("N"), cancellationToken);
        }
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product)
    {
        long? priceInCents = product.PriceInCents;
        return new SubscriptionPlanDto
        {
            Id = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = priceInCents,
            Price = ToPrice(priceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            RequiresPaymentMethod = product.RequireCreditCard
        };
    }

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription)
    {
        long? priceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents;
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = priceInCents,
            Price = ToPrice(priceInCents),
            Interval = subscription.Product?.Interval,
            IntervalUnit = subscription.Product?.IntervalUnit,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    private static decimal ToPrice(long? priceInCents) => priceInCents.HasValue ? priceInCents.Value / 100m : 0m;

    private static string NormalizeUserReference(string userReference)
    {
        return (userReference ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static (string FirstName, string LastName) DeriveDisplayName(string email)
    {
        int at = email.IndexOf('@');
        string localPart = at > 0 ? email.Substring(0, at) : email;
        string[] words = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(TitleCase)
            .Where(w => w.Length > 0)
            .ToArray();

        if (words.Length >= 2)
        {
            return (words[0], string.Join(' ', words.Skip(1)));
        }

        if (words.Length == 1)
        {
            string domain = string.Empty;
            if (at > 0 && at < email.Length - 1)
            {
                string host = email.Substring(at + 1);
                int dot = host.IndexOf('.');
                string firstLabel = dot > 0 ? host.Substring(0, dot) : host;
                domain = TitleCase(firstLabel);
            }

            return (words[0], domain.Length > 0 ? domain : words[0]);
        }

        // Unreachable for a valid email, but guarantees Maxio never receives a blank name.
        return ("eShop", "Subscriber");
    }

    private static string TitleCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
    }
}

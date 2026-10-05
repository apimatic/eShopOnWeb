using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing implementation backed by Maxio Advanced Billing.
/// The Maxio OpenAPI specification (maxio-spec/openapi.yaml) is the contract:
/// all remote calls go through <see cref="IMaxioApiClient"/> whose methods map
/// 1:1 to spec operations. eShopOnWeb users are linked to Maxio via stable
/// reference values keyed on the user's email (the app's username, and the
/// only identifier that is stable even when the identity store is ephemeral):
///   customer reference     = eShopOnWeb user email
///   subscription reference = eshop-{email}-{planHandle}[-{attempt}]
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string SubscriptionReferencePrefix = "eshop";
    private const int MaxReferenceAttempts = 5;

    /// <summary>End-of-life states per the spec's Subscription-State schema;
    /// a subscription in one of these will never come back on its own.</summary>
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly IMaxioApiClient _apiClient;
    private readonly IRepository<SubscriptionLink> _subscriptionLinkRepository;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(IMaxioApiClient apiClient,
        IRepository<SubscriptionLink> subscriptionLinkRepository,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _apiClient = apiClient;
        _subscriptionLinkRepository = subscriptionLinkRepository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _apiClient.ListProductsAsync(cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null)
            .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Select(p => new SubscriptionPlan(p.Handle, p.Name, p.Description, p.PriceInCents,
                p.Interval, p.IntervalUnit, p.ProductFamily?.Handle ?? string.Empty))
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(string userId, string email, string displayName,
        string planHandle, CancellationToken cancellationToken = default)
    {
        GuardInput(userId, email, planHandle);

        var product = await ResolvePlanOrThrowAsync(planHandle, cancellationToken);

        var customerKey = CustomerKey(email);
        var customer = await EnsureCustomerAsync(customerKey, email, displayName, cancellationToken);

        // Local fast-path: a previous successful enrollment for this user+plan
        // that is still live in Maxio short-circuits the double-click case.
        var baseReference = BuildSubscriptionReference(customerKey, planHandle);
        var localLink = (await _subscriptionLinkRepository.ListAsync(
                new SubscriptionLinkByReferenceSpec(baseReference), cancellationToken))
            .FirstOrDefault();
        if (localLink is not null)
        {
            var live = await _apiClient.GetSubscriptionByIdAsync(localLink.MaxioSubscriptionId, cancellationToken);
            if (live is not null && !IsEndOfLife(live))
            {
                return MapEnrollment(live, alreadySubscribed: true);
            }
        }

        // Resolve a free reference: reuse the live subscription if one already
        // exists for this user+plan (idempotent), otherwise pick the first
        // reference not taken by an end-of-life subscription.
        MaxioSubscription? existing = null;
        var reference = baseReference;
        for (var attempt = 1; attempt <= MaxReferenceAttempts; attempt++)
        {
            existing = await _apiClient.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (existing is null)
            {
                break;
            }
            if (!IsEndOfLife(existing))
            {
                return MapEnrollment(existing, alreadySubscribed: true);
            }
            if (attempt == MaxReferenceAttempts)
            {
                throw new InvalidOperationException(
                    $"Could not resolve a free subscription reference for user '{customerKey}' and plan '{planHandle}'.");
            }
            reference = $"{baseReference}-{attempt + 1}";
        }

        var subscription = await _apiClient.CreateSubscriptionAsync(
            new CreateMaxioSubscriptionRequest
            {
                Subscription = new CreateMaxioSubscriptionPayload
                {
                    ProductHandle = product.Handle,
                    CustomerId = customer.Id,
                    Reference = reference
                }
            }, cancellationToken);

        _logger.LogInformation($"Created Maxio subscription {subscription.Id} (ref {reference}) " +
                               $"for user {customerKey} on plan {product.Handle}.");

        await UpsertLinkAsync(userId, customer.Id, subscription, cancellationToken);

        return MapEnrollment(subscription, alreadySubscribed: false);
    }

    public async Task<IReadOnlyList<SubscriptionEnrollment>> GetSubscriptionsForUserAsync(string userId,
        string email, string displayName, CancellationToken cancellationToken = default)
    {
        GuardInput(userId, email, "any");

        var customerKey = CustomerKey(email);
        var customer = await _apiClient.GetCustomerByReferenceAsync(customerKey, cancellationToken);
        if (customer is null)
        {
            return new List<SubscriptionEnrollment>();
        }

        var subscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        foreach (var subscription in subscriptions)
        {
            await UpsertLinkAsync(userId, customer.Id, subscription, cancellationToken);
        }

        return subscriptions.Select(s => MapEnrollment(s, alreadySubscribed: false)).ToList();
    }

    private async Task<MaxioProduct> ResolvePlanOrThrowAsync(string planHandle, CancellationToken cancellationToken)
    {
        var product = await _apiClient.GetProductByHandleAsync(planHandle, cancellationToken);
        if (product is null ||
            !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnknownSubscriptionPlanException(planHandle);
        }
        return product;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string customerKey, string email, string displayName,
        CancellationToken cancellationToken)
    {
        var existing = await _apiClient.GetCustomerByReferenceAsync(customerKey, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(displayName, email);
        var customer = await _apiClient.CreateCustomerAsync(
            new CreateMaxioCustomerRequest
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = customerKey
            }, cancellationToken) ?? throw new InvalidOperationException(
            $"Maxio did not return a customer for user '{customerKey}' after creation.");

        _logger.LogInformation($"Created Maxio customer {customer.Id} for eShopOnWeb user {customerKey}.");

        return customer;
    }

    private async Task UpsertLinkAsync(string userId, int maxioCustomerId, MaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        if (subscription.Id <= 0 || string.IsNullOrWhiteSpace(subscription.Reference))
        {
            return;
        }

        try
        {
            var link = (await _subscriptionLinkRepository.ListAsync(
                    new SubscriptionLinkByReferenceSpec(subscription.Reference), cancellationToken))
                .FirstOrDefault();

            if (link is null)
            {
                await _subscriptionLinkRepository.AddAsync(new SubscriptionLink(
                    userId, subscription.Reference,
                    subscription.Product?.Handle ?? string.Empty,
                    maxioCustomerId, subscription.Id), cancellationToken);
            }
            else
            {
                link.UpdateFromMaxio(maxioCustomerId, subscription.Id,
                    subscription.Product?.Handle ?? link.ProductHandle);
                await _subscriptionLinkRepository.UpdateAsync(link, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // The local mirror is best-effort; Maxio remains the system of record.
            _logger.LogWarning($"Failed to persist local subscription link for reference {subscription.Reference}: {ex.Message}");
        }
    }

    private static SubscriptionEnrollment MapEnrollment(MaxioSubscription subscription, bool alreadySubscribed)
    {
        return new SubscriptionEnrollment(
            subscription.Id,
            subscription.Reference ?? string.Empty,
            subscription.Customer?.Id.ToString() ?? string.Empty,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            subscription.ProductPriceInCents,
            subscription.State,
            subscription.CurrentPeriodEndsAt,
            subscription.ActivatedAt,
            subscription.CreatedAt ?? DateTimeOffset.UtcNow,
            alreadySubscribed);
    }

    private static bool IsEndOfLife(MaxioSubscription subscription)
        => EndOfLifeStates.Contains(subscription.State);

    /// <summary>
    /// Builds the deterministic subscription reference for a user+plan.
    /// </summary>
    public static string BuildSubscriptionReference(string userKey, string planHandle)
        => $"{SubscriptionReferencePrefix}-{userKey}-{planHandle}".ToLowerInvariant();

    /// <summary>
    /// The stable key used as the Maxio customer reference: the user's email
    /// (which is the username in eShopOnWeb), normalized to lowercase.
    /// </summary>
    private static string CustomerKey(string email)
        => email.Trim().ToLowerInvariant();

    private static void GuardInput(string userId, string email, string planHandle)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("User email is required.", nameof(email));
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("Plan handle is required.", nameof(planHandle));
        }
    }

    private static (string FirstName, string LastName) SplitName(string displayName, string email)
    {
        var name = (displayName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                return (parts[0], parts[1]);
            }
            if (parts.Length == 1 && !parts[0].Contains('@'))
            {
                return (parts[0], "Customer");
            }
        }

        var localPart = (email ?? string.Empty).Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? ("eShopOnWeb", "Customer") : (localPart, "Customer");
    }
}
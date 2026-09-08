using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class SubscribeToPlanResult
{
    public SubscriptionDto Subscription { get; init; } = new();
    public bool NewlyCreated { get; init; }
}

/// <summary>
/// Application service that turns an eShopOnWeb shopper + plan handle into a
/// Maxio customer and subscription. Idempotency is guaranteed by Maxio's unique
/// customer/subscription reference constraints: two overlapping requests for the
/// same user+plan converge on a single customer and a single subscription.
/// </summary>
public class MaxioSubscriptionService
{
    private const string SubscriptionReferencePrefix = "eshop-sub";
    private const string CustomerReferencePrefix = "eshop";

    private static readonly string[] TerminalSubscriptionStates = { "canceled", "expired", "failed_to_create" };

    private readonly IMaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListAvailablePlansAsync(
        CancellationToken cancellationToken = default)
    {
        _options.EnsureValid();

        var currency = await GetSiteCurrencyAsync(cancellationToken);
        var products = await GetCatalogProductsAsync(cancellationToken);

        return products
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = CentsToAmount(p.PriceInCents),
                Currency = currency,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            })
            .ToList();
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureValid();

        var customer = await FindCustomerByReferenceAsync(BuildCustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Select(Map)
            .ToList();
    }

    public async Task<SubscribeToPlanResult> SubscribeAsync(
        ApplicationUser user,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureValid();

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new InvalidSubscriptionRequestException("planHandle is required.");
        }

        var products = await GetCatalogProductsAsync(cancellationToken);
        var plan = products.FirstOrDefault(p =>
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var customer = await EnsureCustomerAsync(user, cancellationToken);

        var customerSubscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var subscriptionsForPlan = customerSubscriptions
            .Where(s => string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var liveSubscription = subscriptionsForPlan.FirstOrDefault(s => !IsTerminalState(s.State));
        if (liveSubscription is not null)
        {
            // Already subscribed to this plan - return the existing subscription so a
            // double-click/retry never produces a second one.
            return new SubscribeToPlanResult { Subscription = Map(liveSubscription), NewlyCreated = false };
        }

        var generation = subscriptionsForPlan.Count + 1;
        var subscriptionReference =
            $"{SubscriptionReferencePrefix}:{user.Id}:{plan.Handle}:{generation}";

        MaxioSubscription created;
        try
        {
            created = await _client.CreateSubscriptionAsync(
                plan.Handle,
                customer.Reference,
                subscriptionReference,
                cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} (reference {Reference}) for eShop user {UserId}.",
                created.Id, subscriptionReference, user.Id);

            return new SubscribeToPlanResult { Subscription = Map(created), NewlyCreated = true };
        }
        catch (MaxioApiException ex) when (ex.IsReferenceConflict)
        {
            // Another overlapping request already created a subscription with this
            // deterministic reference; resolve it and replay the result idempotently.
            var existing = await FindSubscriptionWithRetryAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Maxio subscription reference {Reference} already exists; returning subscription {SubscriptionId}.",
                    subscriptionReference, existing.Id);

                return new SubscribeToPlanResult { Subscription = Map(existing), NewlyCreated = false };
            }

            throw;
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customerReference = BuildCustomerReference(user.Id);
        var email = ResolveEmail(user);

        var existing = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveNameFromEmail(email);

        try
        {
            var created = await _client.CreateCustomerAsync(customerReference, email, firstName, lastName, cancellationToken);
            _logger.LogInformation(
                "Created Maxio customer {CustomerId} (reference {Reference}) for eShop user {UserId}.",
                created.Id, customerReference, user.Id);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsReferenceConflict)
        {
            // Lost an interleaved create against another request; the winner is now findable.
            var winner = await FindCustomerWithRetryAsync(customerReference, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        return await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
    }

    private async Task<MaxioCustomer?> FindCustomerWithRetryAsync(string reference, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var customer = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (customer is not null)
            {
                return customer;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)), cancellationToken);
        }

        return null;
    }

    private async Task<MaxioSubscription?> FindSubscriptionWithRetryAsync(string reference, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var subscription = await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (subscription is not null)
            {
                return subscription;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)), cancellationToken);
        }

        return null;
    }

    private async Task<IReadOnlyList<MaxioProduct>> GetCatalogProductsAsync(CancellationToken cancellationToken)
    {
        var key = $"maxio:catalog-products:{_options.ProductFamilyHandle}";
        if (_cache.TryGetValue(key, out IReadOnlyList<MaxioProduct>? cached) && cached is not null)
        {
            return cached;
        }

        var products = await _client.ListProductFamilyProductsAsync(_options.ProductFamilyHandle!, cancellationToken);
        _cache.Set(key, products, TimeSpan.FromMinutes(5));
        return products;
    }

    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        var key = $"maxio:site-currency:{_options.ProductFamilyHandle}";
        if (_cache.TryGetValue(key, out string? cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        var site = await _client.GetSiteAsync(cancellationToken);
        _cache.Set(key, site.Currency, TimeSpan.FromHours(1));
        return site.Currency;
    }

    private static string BuildCustomerReference(string userId) =>
        $"{CustomerReferencePrefix}-{userId}";

    private static bool IsTerminalState(string state) =>
        TerminalSubscriptionStates.Contains(state, StringComparer.OrdinalIgnoreCase);

    private static decimal CentsToAmount(long cents) =>
        decimal.Round(cents / 100m, 2, MidpointRounding.AwayFromZero);

    private static string ResolveEmail(ApplicationUser user) =>
        !string.IsNullOrWhiteSpace(user.Email) ? user.Email : user.UserName ?? string.Empty;

    private static (string FirstName, string LastName) DeriveNameFromEmail(string email)
    {
        // eShopOnWeb does not capture a shopper's name at registration, so derive a
        // stable first/last name from the email address local-part.
        var localPart = email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return ("eShop", "Shopper");
        }

        var firstName = Capitalize(parts[0]);
        var lastName = parts.Length > 1
            ? string.Join(" ", parts.Skip(1).Select(Capitalize))
            : "Shopper";

        return (Truncate(firstName, 60), Truncate(lastName, 60));
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value.Substring(0, maxLength);

    private static SubscriptionDto Map(MaxioSubscription subscription)
    {
        var product = subscription.Product;

        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            Price = CentsToAmount(subscription.ProductPriceInCents),
            Currency = subscription.Currency,
            Interval = product?.Interval ?? 1,
            IntervalUnit = product?.IntervalUnit ?? string.Empty,
            BalanceInCents = subscription.BalanceInCents,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt
        };
    }
}

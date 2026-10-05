using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Orchestrates subscription billing: resolves (idempotently) a Maxio customer for an
/// eShopOnWeb user, lists subscribable plans, enrolls the user, and lists their subscriptions.
/// Maxio is the billing system of record; no subscription data is persisted locally.
/// The eShopOnWeb user id travels as the Maxio customer <c>reference</c>, which Maxio
/// enforces as unique — that makes customer creation idempotent even under races.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default);

    /// <summary>Subscribes the user to the plan, or returns the existing active subscription for it.</summary>
    Task<(SubscriptionDto Subscription, bool AlreadySubscribed, bool CustomerCreated)> SubscribeAsync(
        string userId, string username, string email, string planHandle, CancellationToken ct = default);

    /// <summary>Lists the user's subscriptions; empty when the user has no Maxio customer yet.</summary>
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(string userId, CancellationToken ct = default);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioClient _maxio;
    private readonly MaxioOptions _options;

    /// <summary>
    /// Serializes subscribe operations per user so a double-click cannot race past the
    /// "already subscribed?" check and create two customers/subscriptions (single instance).
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    /// <summary>Subscription states that count as "already subscribed" for idempotency.</summary>
    private static readonly HashSet<string> NonCancelStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "past_due", "on_hold", "unpaid", "suspended", "pending"
    };

    public SubscriptionService(IMaxioClient maxio, IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default)
    {
        var products = await _maxio.ListFamilyProductsAsync(_options.ProductFamilyHandle, ct);
        return products
            .Where(p => p.ArchivedAt == null)
            .Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = p.PriceInCents / 100m,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty
            })
            .OrderBy(p => p.Price)
            .ToList();
    }

    public async Task<(SubscriptionDto Subscription, bool AlreadySubscribed, bool CustomerCreated)> SubscribeAsync(
        string userId, string username, string email, string planHandle, CancellationToken ct = default)
    {
        var plan = (await GetPlansAsync(ct)).FirstOrDefault(p =>
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan == null)
        {
            throw new UnknownPlanException($"Unknown plan handle '{planHandle}'. It must be a product of the configured Maxio product family '{_options.ProductFamilyHandle}'.");
        }

        var userLock = _userLocks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(ct);
        try
        {
            var (customer, customerCreated) = await EnsureCustomerAsync(userId, username, email, ct);

            var existing = (await _maxio.ListCustomerSubscriptionsAsync(customer.Id, ct))
                .FirstOrDefault(s =>
                    string.Equals(s.Product.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                    NonCancelStates.Contains(s.State));
            if (existing != null)
            {
                return (MapSubscription(existing), AlreadySubscribed: true, CustomerCreated: false);
            }

            var created = await _maxio.CreateSubscriptionAsync(plan.Handle, customer.Id, ct);
            return (MapSubscription(created), AlreadySubscribed: false, CustomerCreated: customerCreated);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(userId, ct);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDto>();
        }
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, ct);
        return subscriptions.Select(MapSubscription).OrderByDescending(s => s.CreatedAt).ToList();
    }

    /// <summary>
    /// Returns the Maxio customer for the user, creating one on first use.
    /// Lookup-by-reference first; on a create-conflict (unique-reference validation from a
    /// concurrent request) the lookup is retried, so double clicks never yield two customers.
    /// </summary>
    private async Task<(MaxioCustomer Customer, bool Created)> EnsureCustomerAsync(
        string userId, string username, string email, CancellationToken ct)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(userId, ct);
        if (existing != null)
        {
            return (existing, Created: false);
        }

        var (firstName, lastName) = SplitName(username, email);
        try
        {
            var created = await _maxio.CreateCustomerAsync(firstName, lastName, email, userId, ct);
            return (created, Created: true);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var racedCustomer = await _maxio.FindCustomerByReferenceAsync(userId, ct);
            if (racedCustomer != null)
            {
                return (racedCustomer, Created: false);
            }
            throw;
        }
    }

    /// <summary>
    /// Maxio requires first/last names; eShopOnWeb users only have a username/email.
    /// Derives a deterministic, human-readable pair from the email local part.
    /// </summary>
    private static (string FirstName, string LastName) SplitName(string username, string email)
    {
        var local = (email ?? username ?? "eShop Customer").Split('@')[0];
        var tokens = local.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = Capitalize(tokens.FirstOrDefault() ?? "eShop");
        var lastName = tokens.Length > 1 ? Capitalize(string.Join(" ", tokens.Skip(1))) : "Customer";
        return (firstName, lastName);
    }

    private static string Capitalize(string value)
    {
        return string.IsNullOrEmpty(value) ? value : (char.ToUpperInvariant(value[0]) + value[1..]);
    }

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = subscription.Product.Handle,
            PlanName = subscription.Product.Name,
            Price = (subscription.Product.PriceInCents > 0 ? subscription.Product.PriceInCents : subscription.ProductPriceInCents) / 100m,
            State = subscription.State,
            NextBillingDate = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.ActivatedAt ?? subscription.CreatedAt
        };
    }
}
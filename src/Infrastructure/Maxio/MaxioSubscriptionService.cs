using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the eShopOnWeb ↔ Maxio Advanced Billing subscription flow: it keeps a
/// Billing API customer in sync with each eShopOnWeb user (idempotently), enrolls
/// shoppers into plans without creating duplicates, and projects their subscriptions.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available for purchase.
    /// </summary>
    Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the Billing API customer that represents the given eShopOnWeb user,
    /// creating it on first use. Idempotent: repeated and concurrent calls never create
    /// more than one customer for the same user.
    /// </summary>
    Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the user into the plan identified by its product handle. Idempotent:
    /// if the user already has a live subscription to the plan it is returned as-is
    /// (a double-click can never create a second subscription or customer).
    /// </summary>
    Task<MaxioSubscriptionSummary> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all of the user's subscriptions in Billing API.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscriptionSummary>> GetSubscriptionsForUserAsync(ApplicationUser user, CancellationToken cancellationToken = default);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly IMaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IMaxioApiClient client, Microsoft.Extensions.Options.IOptions<MaxioOptions> options, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw MaxioConfigurationException.MissingSettings("Maxio:ProductFamilyHandle is required to list subscription plans.");
        }

        var products = await _client.ListProductsInFamilyAsync(_options.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt == null)
            .Select(MapPlan)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var reference = CustomerReferenceFor(user);
        var existing = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(user);
        try
        {
            var created = await _client.CreateCustomerAsync(new MaxioCustomerInput
            {
                Reference = reference,
                FirstName = firstName,
                LastName = lastName,
                Email = user.Email ?? user.UserName ?? string.Empty,
                Organization = "eShopOnWeb"
            }, cancellationToken);

            _logger.LogInformation("Created Billing API customer {CustomerId} (reference {Reference}) for eShopOnWeb user {UserId}.",
                created.Id, reference, user.Id);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request may have created the customer between our lookup and
            // create. Billing API enforces one customer per reference value, so re-lookup.
            var raced = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced != null)
            {
                _logger.LogInformation("Lost a race creating Billing API customer for user {UserId}; using existing customer {CustomerId}.",
                    user.Id, raced.Id);
                return raced;
            }

            throw;
        }
    }

    public async Task<MaxioSubscriptionSummary> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan == null)
        {
            throw new MaxioPlanNotFoundException(planHandle);
        }

        var customer = await EnsureCustomerAsync(user, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customer, plan, cancellationToken);
        if (existing != null)
        {
            _logger.LogInformation("User {UserId} is already subscribed to plan '{PlanHandle}' (subscription {SubscriptionId}); returning it.",
                user.Id, planHandle, existing.SubscriptionId);
            return existing;
        }

        // Deterministic uniqueness token: concurrent double-submits of the same
        // (customer, plan) pair are rejected by Billing API's duplicate prevention
        // instead of creating a second subscription.
        var uniquenessToken = DeterministicToken($"eshop-subscribe:{customer.Id}:{plan.ProductId}");

        try
        {
            var subscription = await _client.CreateSubscriptionAsync(customer.Id, plan.Handle!, uniquenessToken, cancellationToken);
            _logger.LogInformation("User {UserId} subscribed to '{PlanHandle}' (subscription {SubscriptionId}).",
                user.Id, planHandle, subscription.Id);
            return MapSummary(subscription);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // The token was seen before: the first submit may have created the
            // subscription. Reconcile against the customer's subscriptions; only retry
            // with a fresh token when nothing was actually created.
            var reconciled = await FindLiveSubscriptionAsync(customer, plan, cancellationToken);
            if (reconciled != null)
            {
                return reconciled;
            }

            var retryToken = Guid.NewGuid().ToString("N");
            var subscription = await _client.CreateSubscriptionAsync(customer.Id, plan.Handle!, retryToken, cancellationToken);
            _logger.LogInformation("User {UserId} subscribed to '{PlanHandle}' (subscription {SubscriptionId}) after duplicate-prevention retry.",
                user.Id, planHandle, subscription.Id);
            return MapSummary(subscription);
        }
    }

    public async Task<IReadOnlyList<MaxioSubscriptionSummary>> GetSubscriptionsForUserAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .Select(MapSummary)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    private async Task<MaxioSubscriptionSummary?> FindLiveSubscriptionAsync(MaxioCustomer customer, MaxioPlan plan, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(s => s.IsLive() && s.Product != null && string.Equals(s.Product.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
            .Select(MapSummary)
            .FirstOrDefault();
    }

    private static string CustomerReferenceFor(ApplicationUser user)
    {
        var reference = string.IsNullOrWhiteSpace(user.Id) ? null : user.Id.Trim();
        if (string.IsNullOrEmpty(reference))
        {
            throw new InvalidOperationException("The eShopOnWeb user has no id to reference in Billing API.");
        }

        return reference;
    }

    private static (string FirstName, string LastName) SplitName(ApplicationUser user)
    {
        var email = user.Email ?? user.UserName ?? string.Empty;
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var separators = new[] { '.', '_', '-', '+' };
        var parts = localPart.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return (Capitalize(parts[0]), Capitalize(parts[1]));
        }

        if (parts.Length == 1)
        {
            return (Capitalize(parts[0]), "Shopper");
        }

        return ("eShop", "Shopper");
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string DeterministicToken(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static MaxioPlan MapPlan(MaxioProduct product) => new()
    {
        ProductId = product.Id,
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequireCreditCard = product.RequireCreditCard,
        Taxable = product.Taxable,
        ProductFamilyHandle = product.ProductFamily?.Handle
    };

    private static MaxioSubscriptionSummary MapSummary(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State,
        CustomerId = subscription.Customer?.Id ?? subscription.CustomerId,
        CustomerReference = subscription.Customer?.Reference,
        ProductId = subscription.ProductId ?? subscription.Product?.Id,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        CanceledAt = subscription.CanceledAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        IsLive = subscription.IsLive()
    };
}

/// <summary>
/// Thrown when a shopper asks to subscribe to a plan that is not offered.
/// </summary>
public class MaxioPlanNotFoundException : KeyNotFoundException
{
    public string PlanHandle { get; }

    public MaxioPlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is offered.")
    {
        PlanHandle = planHandle;
    }
}
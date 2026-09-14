using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Subscription capabilities exposed to the API layer. Maxio Advanced Billing is
/// the system of record; the eShopOnWeb user is linked to a Maxio customer via a
/// unique customer reference (the user's sign-in name / email).
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for the current user and subscribes them to
    /// the given plan. Idempotent: subscribing twice to the same plan returns the
    /// existing subscription instead of creating a second one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListMySubscriptionsAsync(CancellationToken cancellationToken);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "past_due", "soft_failure", "assessing", "pending", "awaiting_signup"
    };

    private readonly IMaxioApiClient _api;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly KeyedLock _locks;
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioApiClient api,
        IHttpContextAccessor httpContextAccessor,
        KeyedLock locks,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionService> logger)
    {
        _api = api;
        _httpContextAccessor = httpContextAccessor;
        _locks = locks;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var familyHandle = _options.Value.ProductFamilyHandle;
        var products = await _api.ListProductsForFamilyAsync(familyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string planHandle, CancellationToken cancellationToken)
    {
        var userName = CurrentUserName();
        var trimmedPlanHandle = (planHandle ?? string.Empty).Trim();
        if (trimmedPlanHandle.Length == 0)
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        // Serialize subscribes per user so a double-click/parallel request from
        // the same user can never race past the idempotency checks below.
        using var _ = await _locks.WaitAsync("subscribe:" + userName, cancellationToken);

        var customer = await EnsureCustomerAsync(userName, cancellationToken);
        var customerId = RequireCustomerId(customer);

        var existing = await FindLiveSubscriptionAsync(customerId, trimmedPlanHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("User {UserName} is already subscribed to plan {PlanHandle} (Maxio subscription {SubscriptionId}).",
                userName, trimmedPlanHandle, existing.Id);
            return new SubscribeResult(existing, AlreadySubscribed: true);
        }

        var request = new MaxioSubscriptionCreate
        {
            CustomerId = customerId,
            ProductHandle = trimmedPlanHandle,
            PaymentCollectionMethod = "remittance"
        };

        var uniquenessToken = BuildUniquenessToken(userName, trimmedPlanHandle);
        try
        {
            var created = await _api.CreateSubscriptionAsync(request, uniquenessToken, cancellationToken);
            _logger.LogInformation("User {UserName} subscribed to plan {PlanHandle} (Maxio subscription {SubscriptionId}).",
                userName, trimmedPlanHandle, created.Id);
            return new SubscribeResult(created, AlreadySubscribed: false);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 409)
        {
            return await ReconcileDuplicateAsync(customerId, trimmedPlanHandle, request, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListMySubscriptionsAsync(CancellationToken cancellationToken)
    {
        var userName = CurrentUserName();
        var customer = await _api.FindCustomerByReferenceAsync(userName, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var customerId = RequireCustomerId(customer);
        var subscriptions = await _api.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userName, CancellationToken cancellationToken)
    {
        var existing = await _api.FindCustomerByReferenceAsync(userName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _api.CreateCustomerAsync(BuildCustomer(userName), cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserName}.", created.Id, userName);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Another concurrent request may have created the customer between our
            // lookup and create. Re-lookup before failing.
            var concurrent = await _api.FindCustomerByReferenceAsync(userName, cancellationToken);
            if (concurrent is not null)
            {
                return concurrent;
            }

            throw;
        }
    }

    private async Task<SubscribeResult> ReconcileDuplicateAsync(
        long customerId,
        string planHandle,
        MaxioSubscriptionCreate request,
        CancellationToken cancellationToken)
    {
        // A 409 means a previous request with the same uniqueness token was
        // received within the dedupe window. If that request produced a live
        // subscription, return it (this is a true duplicate). If it did not
        // (for example, the user canceled and resubscribed), retry once with a
        // fresh token to create a genuinely new subscription.
        var delayMs = 300;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(delayMs, cancellationToken);
            delayMs *= 2;

            var current = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
            if (current is not null)
            {
                return new SubscribeResult(current, AlreadySubscribed: true);
            }
        }

        var retried = await _api.CreateSubscriptionAsync(request, Guid.NewGuid().ToString("N"), cancellationToken);
        return new SubscribeResult(retried, AlreadySubscribed: false);
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _api.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            LiveStates.Contains(s.State) &&
            string.Equals(s.ProductHandle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private string CurrentUserName()
    {
        var name = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("An authenticated user is required for this operation.");
        }

        return name;
    }

    private static MaxioCustomer BuildCustomer(string userName)
    {
        var (firstName, lastName) = DeriveDisplayName(userName);
        return new MaxioCustomer
        {
            FirstName = firstName,
            LastName = lastName,
            Email = userName,
            Reference = userName,
            Organization = userName
        };
    }

    private static long RequireCustomerId(MaxioCustomer customer)
    {
        return customer.Id ?? throw new MaxioApiException(502, "Maxio returned a customer without an id.");
    }

    private static (string FirstName, string LastName) DeriveDisplayName(string userName)
    {
        var at = userName.IndexOf('@');
        var local = at > 0 ? userName[..at] : userName;
        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 1)
        {
            return (parts[0], string.Concat(parts.Skip(1)));
        }

        return (local.Length > 0 ? local : "Shopper", "User");
    }

    private static string BuildUniquenessToken(string userName, string planHandle)
    {
        var input = $"eshop-subscribe|{userName}|{planHandle}".ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

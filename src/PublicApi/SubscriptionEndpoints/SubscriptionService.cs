using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The eShopOnWeb user is mapped to a Maxio customer via the Maxio customer
/// "reference" field, which stores the eShopOnWeb user id. This mapping lives
/// in Maxio (the billing system of record), so it survives application
/// restarts independent of the local database.
///
/// Idempotency: lookups by reference precede every create, per-user requests
/// are serialized, and a live subscription to the same plan short-circuits a
/// duplicate enrollment. The Maxio uniqueness_token guards against transport
/// level retries.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    // Subscription states under which an existing subscription satisfies a
    // new subscribe request. End-of-life states (canceled, expired,
    // trial_ended, failed_to_create) allow the shopper to enroll again.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "trialing", "assessing", "active", "soft_failure",
        "past_due", "unpaid", "suspended", "on_hold", "awaiting_signup"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);

    private readonly IMaxioBillingClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _maxioOptions;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(IMaxioBillingClient maxioClient,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioOptions> maxioOptions,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _maxioOptions = maxioOptions.Value;
        _logger = logger;
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userName, cancellationToken);

        await ValidatePlanAsync(productHandle, cancellationToken);

        var semaphore = UserLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(user, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(customer.Id, productHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("User {UserId} already has a live subscription {SubscriptionId} to plan {ProductHandle}.",
                    user.Id, existing.Id, productHandle);
                return Map(existing);
            }

            var subscription = await _maxioClient.CreateSubscriptionAsync(
                productHandle, customer.Id, SubscriptionReference(user.Id, productHandle), cancellationToken);

            _logger.LogInformation("Created subscription {SubscriptionId} for user {UserId} on plan {ProductHandle}.",
                subscription.Id, user.Id, productHandle);
            return Map(subscription);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(userName, cancellationToken);
        var customer = await _maxioClient.GetCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<ApplicationUser> RequireUserAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new UnauthorizedAccessException($"No eShopOnWeb user exists for '{userName}'.");
        }
        return user;
    }

    /// <summary>
    /// Confirms the plan exists in Maxio and belongs to the configured product
    /// family, so callers cannot enroll in an arbitrary product on the site.
    /// </summary>
    private async Task ValidatePlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var product = await _maxioClient.GetProductByHandleAsync(productHandle, cancellationToken);
        if (product is null)
        {
            throw new MaxioApiException(System.Net.HttpStatusCode.NotFound,
                new[] { $"Subscription plan '{productHandle}' was not found." });
        }

        var familyHandle = product.ProductFamily?.Handle;
        if (!string.Equals(familyHandle, _maxioOptions.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new MaxioApiException(System.Net.HttpStatusCode.UnprocessableEntity,
                new[] { $"Product '{productHandle}' is not part of the subscription plan catalog '{_maxioOptions.ProductFamilyHandle}'." });
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        // The Maxio customer reference is unique per site, so looking it up
        // first makes customer creation idempotent. If two concurrent requests
        // both miss, the second create fails with 422 and we re-lookup.
        var existing = await _maxioClient.GetCustomerByReferenceAsync(user.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(user.UserName ?? user.Email ?? "eShop Customer");
        var email = user.Email ?? user.UserName ?? throw new InvalidOperationException(
            "The eShopOnWeb user has no email address to register with Maxio.");

        try
        {
            var created = await _maxioClient.CreateCustomerAsync(user.Id, firstName, lastName, email, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} (reference {Reference}) for user {UserId}.",
                created.Id, user.Id, user.Id);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _maxioClient.GetCustomerByReferenceAsync(user.Id, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            LiveStates.Contains(s.State) &&
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static string SubscriptionReference(string userId, string productHandle) => $"{userId}:{productHandle}";

    private static (string FirstName, string LastName) SplitName(string source)
    {
        var local = source.Contains('@') ? source.Split('@')[0] : source;
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (Capitalize(parts[0]), Capitalize(string.Join(" ", parts.Skip(1))));
        }
        return (Capitalize(local), "Customer");
    }

    private static string Capitalize(string value)
        => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();

    private static SubscriptionDto Map(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        NextBillingAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        CanceledAt = subscription.CanceledAt
    };
}

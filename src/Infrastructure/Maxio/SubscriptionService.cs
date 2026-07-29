using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the Subscribe flow against Maxio. Maps an authenticated eShopOnWeb user to a
/// Maxio customer (keyed by the user's stable e-mail reference) and enrolls them in a plan.
///
/// Idempotency is enforced on three levels so a double-click or a network retry never creates a
/// duplicate customer or subscription:
///   1. Customer creation looks up by reference first and recovers from a lost race (Maxio's
///      one-customer-per-reference rule) by re-reading the customer.
///   2. Subscribe checks for an existing live subscription to the same plan and returns it.
///   3. A per-user in-process lock serializes concurrent subscribe attempts for the same user,
///      and a random uniqueness_token guards the create against Maxio-side network retries.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    // Maxio states in which a subscription is considered to still "occupy" a plan, so we must not
    // create a second one. Anything else (canceled, expired, trial_ended, ...) allows re-subscribe.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "paused", "past_due", "soft_failure", "on_hold", "awaiting_signup"
    };

    // Process-wide locks keyed by customer reference so concurrent requests for the same user
    // serialize through the ensure-customer / subscribe critical section.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioClient maxioClient,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioSettings> settings,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new MaxioApiException("No Maxio product family handle is configured (Maxio:ProductFamilyHandle).", 500);
        }

        return _maxioClient.ListProductFamilyPlansAsync(familyHandle, cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(string userName, string? planHandle, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userName);
        var reference = ResolveReference(user);

        // Resolve and validate the target plan against the live catalog (no catalog values are
        // hard-coded). When the caller omits a plan we fall back to the first available plan.
        var plans = await GetAvailablePlansAsync(cancellationToken);
        var plan = ResolvePlan(plans, planHandle);

        var gate = UserLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(user, reference, cancellationToken);

            // Idempotency: if the user already has a live subscription to this plan, return it.
            var existing = (await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .FirstOrDefault(s => string.Equals(s.ProductHandle, plan.Handle, StringComparison.OrdinalIgnoreCase)
                                     && LiveStates.Contains(s.State));

            if (existing is not null)
            {
                _logger.LogInformation("User {Reference} already has a live {Plan} subscription ({SubscriptionId}); returning it.",
                    reference, plan.Handle, existing.Id);
                return new SubscribeResult
                {
                    Subscription = existing,
                    AlreadySubscribed = true,
                    CustomerId = customer.Id,
                    CustomerReference = reference
                };
            }

            SubscriptionSummary created;
            try
            {
                created = await CreateSubscriptionAsync(plan.Handle, reference, cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 409)
            {
                // Duplicate-prevention rejected a retry; the original create succeeded — re-read it.
                _logger.LogWarning("Maxio reported a duplicate subscribe for {Reference}; re-reading the existing subscription.", reference);
                var recovered = (await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                    .FirstOrDefault(s => string.Equals(s.ProductHandle, plan.Handle, StringComparison.OrdinalIgnoreCase)
                                         && LiveStates.Contains(s.State));
                if (recovered is null)
                {
                    throw;
                }

                return new SubscribeResult
                {
                    Subscription = recovered,
                    AlreadySubscribed = true,
                    CustomerId = customer.Id,
                    CustomerReference = reference
                };
            }

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} ({Plan}, {State}) for user {Reference}.",
                created.Id, plan.Handle, created.State, reference);

            return new SubscribeResult
            {
                Subscription = created,
                AlreadySubscribed = false,
                CustomerId = customer.Id,
                CustomerReference = reference
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userName);
        var reference = ResolveReference(user);

        var customer = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        return await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    /// <summary>
    /// Creates the subscription using a non-automatic payment collection method so a plan that
    /// doesn't require a stored payment method can be subscribed to without a card. When the method
    /// isn't explicitly configured, this tries <c>remittance</c> (Relationship Invoicing sites) and
    /// falls back to <c>invoice</c> (statement-based sites) so the same build works on either.
    /// </summary>
    private async Task<SubscriptionSummary> CreateSubscriptionAsync(string productHandle, string reference, CancellationToken cancellationToken)
    {
        var candidates = ResolveCollectionMethods();

        for (var i = 0; i < candidates.Count; i++)
        {
            var method = candidates[i];
            try
            {
                return await _maxioClient.CreateSubscriptionAsync(new NewSubscription
                {
                    ProductHandle = productHandle,
                    CustomerReference = reference,
                    PaymentCollectionMethod = method,
                    UniquenessToken = Guid.NewGuid().ToString("N")
                }, cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 422 && i < candidates.Count - 1 && IsCollectionMethodRejected(ex))
            {
                _logger.LogWarning("Maxio rejected payment_collection_method '{Method}' for this site; retrying with '{Next}'.",
                    method, candidates[i + 1]);
            }
        }

        // Unreachable: the loop either returns or rethrows on the final candidate.
        throw new MaxioApiException("Failed to create the subscription with any supported payment collection method.", 502);
    }

    private IReadOnlyList<string> ResolveCollectionMethods()
        => string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod)
            ? new[] { "remittance", "invoice" }
            : new[] { _settings.PaymentCollectionMethod.Trim() };

    private static bool IsCollectionMethodRejected(MaxioApiException ex)
    {
        if (ex.Errors.Any(e => e.Contains("collection", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return ex.Message.Contains("collection", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ensures a Maxio customer exists for the user, recovering from a concurrent create race.</summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(user.Email ?? user.UserName ?? reference);
        try
        {
            var created = await _maxioClient.CreateCustomerAsync(new NewCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = user.Email ?? user.UserName ?? reference,
                Reference = reference
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for user reference {Reference}.", created.Id, reference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode is 422)
        {
            // Another concurrent request likely created the customer first (reference is unique).
            var recovered = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw;
        }
    }

    private async Task<ApplicationUser> ResolveUserAsync(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new MaxioApiException("The request is not associated with an authenticated user.", 401);
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new MaxioApiException($"No eShopOnWeb user found for '{userName}'.", 401);
        }

        return user;
    }

    /// <summary>The Maxio customer reference: the user's stable e-mail (its natural unique key).</summary>
    private static string ResolveReference(ApplicationUser user)
        => user.Email ?? user.UserName ?? user.Id;

    private static SubscriptionPlan ResolvePlan(IReadOnlyList<SubscriptionPlan> plans, string? planHandle)
    {
        if (plans.Count == 0)
        {
            throw new MaxioApiException("No subscription plans are available in the configured Maxio product family.", 502);
        }

        if (!string.IsNullOrWhiteSpace(planHandle))
        {
            var match = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                var available = string.Join(", ", plans.Select(p => p.Handle));
                throw new MaxioApiException($"Unknown plan '{planHandle}'. Available plans: {available}.", 400);
            }

            return match;
        }

        // No plan specified: default to the first plan the catalog exposes.
        return plans[0];
    }

    private static (string First, string Last) DeriveName(string email)
    {
        var local = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var parts = local.Split(new[] { '.', '_', '+', '-' }, StringSplitOptions.RemoveEmptyEntries);

        var first = parts.Length > 0 ? Capitalize(parts[0]) : "eShopOnWeb";
        var last = parts.Length > 1 ? Capitalize(parts[^1]) : "Subscriber";
        return (first, last);
    }

    private static string Capitalize(string value)
        => string.IsNullOrEmpty(value)
            ? value
            : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
}

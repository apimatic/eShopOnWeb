using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates recurring-subscription billing with Maxio Advanced Billing
/// as the billing system of record. Maxio is the only persistent store for
/// the user-to-subscription mapping:
///   - every eShopOnWeb user maps to exactly one Maxio customer, matched by
///     the customer reference (the stable identity user id);
///   - every enrollment creates at most one live subscription per plan,
///     matched by the product handle on the customer's subscriptions and
///     additionally guarded by a deterministic subscription reference.
/// All operations are serialized per user so a double-click can never create
/// duplicate customers or subscriptions.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private static readonly HashSet<string> BlockingSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        // Live states (per the Subscription State enum in the Maxio spec)
        "pending", "assessing", "trialing", "active", "paused",
        // Problem states that still represent a held entitlement
        "past_due", "soft_failure", "unpaid", "suspended", "on_hold"
    };

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    /// <summary>Per-user locks so concurrent/double-click requests cannot race.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new();

    public SubscriptionService(UserManager<ApplicationUser> userManager,
        IMaxioClient maxioClient,
        IOptions<MaxioOptions> maxioOptions,
        ILogger<SubscriptionService> logger)
    {
        _userManager = userManager;
        _maxioClient = maxioClient;
        _options = maxioOptions.Value;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<MaxioPlan>>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var configurationResult = ValidateConfiguration();
        if (!configurationResult.IsSuccess)
        {
            return Result<IReadOnlyList<MaxioPlan>>.Error(configurationResult.Errors.First());
        }

        try
        {
            var products = await _maxioClient.ListProductsAsync(cancellationToken);
            var plans = products
                .Where(p => p.ArchivedAt == null)
                .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
                .Select(p => new MaxioPlan
                {
                    ProductId = p.Id,
                    Handle = p.Handle ?? string.Empty,
                    Name = p.Name,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit,
                    ProductFamilyHandle = p.ProductFamily?.Handle
                })
                .OrderBy(p => p.PriceInCents)
                .ToList();

            return Result<IReadOnlyList<MaxioPlan>>.Success(plans);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Failed to list subscription plans from Maxio.");
            return Result<IReadOnlyList<MaxioPlan>>.Error(MapMaxioFailure(ex));
        }
    }

    public async Task<Result<SubscriptionEnrollment>> SubscribeAsync(string userName, string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return Result<SubscriptionEnrollment>.Invalid(new List<ValidationError>
            {
                new() { Identifier = nameof(planHandle), ErrorMessage = "Plan handle is required." }
            });
        }

        var configurationResult = ValidateConfiguration();
        if (!configurationResult.IsSuccess)
        {
            return Result<SubscriptionEnrollment>.Error(configurationResult.Errors.First());
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
        {
            return Result<SubscriptionEnrollment>.Unauthorized();
        }

        var subscriptionUser = new SubscriptionUser(user.Id, user.UserName ?? user.Id, user.Email ?? user.UserName ?? user.Id);

        var lockHandle = _userLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await lockHandle.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(subscriptionUser, planHandle, cancellationToken);
        }
        finally
        {
            lockHandle.Release();
        }
    }

    public async Task<Result<IReadOnlyList<MaxioSubscription>>> GetSubscriptionsForUserAsync(string userName,
        CancellationToken cancellationToken = default)
    {
        var configurationResult = ValidateConfiguration();
        if (!configurationResult.IsSuccess)
        {
            return Result<IReadOnlyList<MaxioSubscription>>.Error(configurationResult.Errors.First());
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
        {
            return Result<IReadOnlyList<MaxioSubscription>>.Unauthorized();
        }

        try
        {
            var customer = await _maxioClient.LookupCustomerByReferenceAsync(user.Id, cancellationToken);
            if (customer == null)
            {
                // The user has never enrolled: an empty list, not an error.
                return Result<IReadOnlyList<MaxioSubscription>>.Success(new List<MaxioSubscription>());
            }

            var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            return Result<IReadOnlyList<MaxioSubscription>>.Success(subscriptions);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Failed to list subscriptions for user {UserId}.", user.Id);
            return Result<IReadOnlyList<MaxioSubscription>>.Error(MapMaxioFailure(ex));
        }
    }

    private async Task<Result<SubscriptionEnrollment>> SubscribeCoreAsync(SubscriptionUser user, string planHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Validate the plan against the configured product family.
            var plansResult = await ListPlansCoreAsync(cancellationToken);
            if (!plansResult.IsSuccess)
            {
                return Result<SubscriptionEnrollment>.Error(plansResult.Errors.First());
            }
            var plan = plansResult.Value.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (plan == null)
            {
                return Result<SubscriptionEnrollment>.NotFound($"No subscription plan with handle '{planHandle}' exists in the configured product family.");
            }

            // 2. Ensure exactly one Maxio customer exists for this user (idempotent).
            var customer = await EnsureCustomerAsync(user, cancellationToken);

            // 3. If the user already holds a live subscription to this plan, return it
            //    instead of creating a duplicate (double-click safety).
            var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = subscriptions.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                BlockingSubscriptionStates.Contains(s.State ?? string.Empty));
            if (existing != null)
            {
                _logger.LogInformation("User {UserId} is already subscribed to plan {PlanHandle} (Maxio subscription {SubscriptionId}).",
                    user.UserId, planHandle, existing.Id);
                return Result<SubscriptionEnrollment>.Success(new SubscriptionEnrollment(customer, existing, alreadySubscribed: true));
            }

            // 4. Create the subscription. The deterministic reference makes the
            //    create itself idempotent: if a 422 indicates the reference is
            //    taken, the existing subscription is returned instead.
            var reference = BuildSubscriptionReference(user.UserId, planHandle);
            var created = await _maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    Reference = reference
                }
            }, cancellationToken);

            _logger.LogInformation("User {UserId} subscribed to plan {PlanHandle} (Maxio subscription {SubscriptionId}, state {State}).",
                user.UserId, planHandle, created.Id, created.State);
            return Result<SubscriptionEnrollment>.Success(new SubscriptionEnrollment(customer, created, alreadySubscribed: false));
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422 && ex.Errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase)))
        {
            var reference = BuildSubscriptionReference(user.UserId, planHandle);
            var existing = await _maxioClient.LookupSubscriptionByReferenceAsync(reference, cancellationToken);
            if (existing != null && BlockingSubscriptionStates.Contains(existing.State ?? string.Empty))
            {
                var customer = await EnsureCustomerAsync(user, cancellationToken);
                return Result<SubscriptionEnrollment>.Success(new SubscriptionEnrollment(customer, existing, alreadySubscribed: true));
            }
            return Result<SubscriptionEnrollment>.Error(MapMaxioFailure(ex));
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Failed to subscribe user {UserId} to plan {PlanHandle}.", user.UserId, planHandle);
            return Result<SubscriptionEnrollment>.Error(MapMaxioFailure(ex));
        }
    }

    private async Task<Result<IReadOnlyList<MaxioPlan>>> ListPlansCoreAsync(CancellationToken cancellationToken)
    {
        var products = await _maxioClient.ListProductsAsync(cancellationToken);
        var plans = products
            .Where(p => p.ArchivedAt == null)
            .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Select(p => new MaxioPlan
            {
                ProductId = p.Id,
                Handle = p.Handle ?? string.Empty,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                ProductFamilyHandle = p.ProductFamily?.Handle
            })
            .OrderBy(p => p.PriceInCents)
            .ToList();
        return Result<IReadOnlyList<MaxioPlan>>.Success(plans);
    }

    /// <summary>
    /// Finds the Maxio customer for the user by reference, creating it on first
    /// use. Safe against double-execution: the create uses the same stable
    /// reference, and a conflict falls back to a fresh lookup.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriptionUser user, CancellationToken cancellationToken)
    {
        var customer = await _maxioClient.LookupCustomerByReferenceAsync(user.UserId, cancellationToken);
        if (customer != null)
        {
            return customer;
        }

        var request = BuildCreateCustomerRequest(user);
        try
        {
            customer = await _maxioClient.CreateCustomerAsync(request, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} (reference {Reference}) for user {UserId}.",
                customer.Id, customer.Reference, user.UserId);
            return customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Likely a concurrent create of the same reference - re-read instead of failing.
            customer = await _maxioClient.LookupCustomerByReferenceAsync(user.UserId, cancellationToken);
            if (customer != null)
            {
                _logger.LogWarning("Maxio customer create returned 422 for reference {Reference}; resolved by lookup: {CustomerId}.",
                    user.UserId, customer.Id);
                return customer;
            }
            throw;
        }
    }

    private MaxioCreateCustomerRequest BuildCreateCustomerRequest(SubscriptionUser user)
    {
        // Maxio requires first/last name; derive them from the account email.
        var localPart = user.Email.Split('@', StringSplitOptions.TrimEntries)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
        return new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = "Subscriber",
                Email = user.Email,
                Reference = user.UserId,
                Organization = "eShopOnWeb"
            }
        };
    }

    private static string BuildSubscriptionReference(string userId, string planHandle) =>
        $"eshop-sub:{userId}:{planHandle}";

    private Result ValidateConfiguration()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_options.ApiKey)) missing.Add(MaxioOptions.SectionName + ":ApiKey");
        if (string.IsNullOrWhiteSpace(_options.Subdomain)) missing.Add(MaxioOptions.SectionName + ":Subdomain");
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle)) missing.Add(MaxioOptions.SectionName + ":ProductFamilyHandle");
        if (missing.Count > 0)
        {
            return Result.Error($"Maxio configuration is incomplete. Missing: {string.Join(", ", missing)}. " +
                                "Provide the values via user-secrets or environment variables.");
        }
        return Result.Success();
    }

    private static string MapMaxioFailure(MaxioApiException ex)
    {
        var detail = ex.Errors.Count > 0 ? string.Join("; ", ex.Errors) : ex.Message;
        return $"Maxio Advanced Billing request failed (HTTP {ex.StatusCode}): {detail}";
    }
}
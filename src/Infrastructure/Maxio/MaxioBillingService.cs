using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of subscription billing for eShopOnWeb.
///
/// The eShopOnWeb user ID is stored as the Maxio customer <c>reference</c>, which
/// makes the user-to-billing-customer mapping durable in the billing system of
/// record itself (no local persistence required, and it survives application
/// restarts). Enrollment is idempotent: a customer is looked up before it is
/// created, and an existing live subscription on the requested plan short-circuits
/// creation so a double-click can never create duplicates.
/// </summary>
public sealed class MaxioBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// Subscription states that mean "still live". A user with a subscription in one
    /// of these states on the requested plan is not enrolled again. Everything else
    /// (canceled, expired, trial_ended, failed_to_create) allows a fresh signup.
    /// Per Maxio docs, canceled/expired are End-of-Life states; on_hold/unpaid/past_due
    /// remain live because the customer still holds the subscription.
    /// </summary>
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "trial_ended", "failed_to_create"
    };

    private readonly IMaxioApiClient _api;
    private readonly MaxioOptions _options;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        IMaxioApiClient api,
        IOptions<MaxioOptions> options,
        UserManager<ApplicationUser> userManager,
        ILogger<MaxioBillingService> logger)
    {
        _api = api;
        _options = options.Value;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Result<IEnumerable<SubscriptionPlanInfo>>> GetPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            var products = await _api.ListProductsAsync(cancellationToken);
            var plans = products
                .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
                .Where(p => p.ArchivedAt == null)
                .Select(MapPlan)
                .ToList();

            if (plans.Count == 0 &&
                !products.Any(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase)))
            {
                return Result<IEnumerable<SubscriptionPlanInfo>>.Error(
                    $"The configured Maxio product family '{_options.ProductFamilyHandle}' has no products. " +
                    $"Verify 'Maxio:{nameof(MaxioOptions.ProductFamilyHandle)}'.");
            }

            return Result<IEnumerable<SubscriptionPlanInfo>>.Success(plans);
        }
        catch (MaxioApiException ex)
        {
            return ErrorPlans(ex, "listing subscription plans");
        }
        catch (InvalidOperationException ex)
        {
            return Result<IEnumerable<SubscriptionPlanInfo>>.Error(ex.Message);
        }
    }

    public async Task<Result<SubscriptionEnrollment>> SubscribeAsync(
        string userName, string planHandle, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return Result<SubscriptionEnrollment>.Invalid(new List<ValidationError>
            {
                new() { Identifier = nameof(planHandle), ErrorMessage = "Plan handle is required." }
            });
        }

        try
        {
            var user = await RequireUserAsync(userName);
            if (user == null)
            {
                return Result<SubscriptionEnrollment>.NotFound($"No account found for user '{userName}'.");
            }

            var product = await _api.GetProductByHandleAsync(planHandle, cancellationToken);
            if (product == null ||
                !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            {
                return Result<SubscriptionEnrollment>.NotFound($"Unknown subscription plan '{planHandle}'.");
            }

            if (product.ArchivedAt != null)
            {
                return Result<SubscriptionEnrollment>.NotFound($"Subscription plan '{planHandle}' is no longer available.");
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);

            var existing = await _api.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var liveMatch = existing.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, product.Handle, StringComparison.OrdinalIgnoreCase) &&
                !EndOfLifeStates.Contains(s.State ?? string.Empty));

            if (liveMatch != null)
            {
                _logger.LogInformation(
                    "User {UserId} already has subscription {SubscriptionId} (state {State}) on plan {PlanHandle}; returning it.",
                    user.Id, liveMatch.Id, liveMatch.State, product.Handle);

                return Result<SubscriptionEnrollment>.Success(new SubscriptionEnrollment
                {
                    Subscription = MapSubscription(liveMatch),
                    BillingCustomerId = customer.Id,
                    BillingCustomerReference = user.Id,
                    AlreadySubscribed = true
                });
            }

            var uniquenessToken = string.IsNullOrWhiteSpace(idempotencyKey)
                ? Guid.NewGuid().ToString("N")
                : idempotencyKey.Trim();

            var created = await _api.CreateSubscriptionAsync(product.Handle!, customer.Id, uniquenessToken, cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} (state {State}) for user {UserId} on plan {PlanHandle}.",
                created.Id, created.State, user.Id, product.Handle);

            return Result<SubscriptionEnrollment>.Success(new SubscriptionEnrollment
            {
                Subscription = MapSubscription(created),
                BillingCustomerId = customer.Id,
                BillingCustomerReference = user.Id,
                AlreadySubscribed = false
            });
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 409)
        {
            _logger.LogWarning(
                "Maxio rejected a subscription create as a duplicate (uniqueness token {Token}); a previous request likely succeeded.",
                idempotencyKey);
            return Result<SubscriptionEnrollment>.Error(
                "The subscription request was flagged as a duplicate by the billing system. " +
                "Retry the read of your subscriptions; do not re-submit the same idempotency key with new intent.");
        }
        catch (MaxioApiException ex)
        {
            return ErrorEnrollment(ex, $"enrolling '{userName}' in plan '{planHandle}'");
        }
        catch (InvalidOperationException ex)
        {
            return Result<SubscriptionEnrollment>.Error(ex.Message);
        }
    }

    public async Task<Result<IEnumerable<SubscriptionSummary>>> GetSubscriptionsForUserAsync(
        string userName, CancellationToken cancellationToken)
    {
        try
        {
            var user = await RequireUserAsync(userName);
            if (user == null)
            {
                return Result<IEnumerable<SubscriptionSummary>>.NotFound($"No account found for user '{userName}'.");
            }

            var customer = await _api.FindCustomerByReferenceAsync(user.Id, cancellationToken);
            if (customer == null)
            {
                // The user has never been enrolled; an empty list is the correct answer.
                return Result<IEnumerable<SubscriptionSummary>>.Success(Array.Empty<SubscriptionSummary>());
            }

            var subscriptions = await _api.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            return Result<IEnumerable<SubscriptionSummary>>.Success(subscriptions.Select(MapSubscription).ToList());
        }
        catch (MaxioApiException ex)
        {
            return ErrorSummaries(ex, $"listing subscriptions for '{userName}'");
        }
        catch (InvalidOperationException ex)
        {
            return Result<IEnumerable<SubscriptionSummary>>.Error(ex.Message);
        }
    }

    private async Task<ApplicationUser?> RequireUserAsync(string userName)
    {
        return string.IsNullOrWhiteSpace(userName)
            ? null
            : await _userManager.FindByNameAsync(userName);
    }

    /// <summary>
    /// Finds the Maxio customer for the eShopOnWeb user, creating it on first use.
    /// Safe under concurrency: if creation loses a race (unique-reference violation),
    /// the winning customer is looked back up.
    /// </summary>
    private async Task<MaxioCustomerDto> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var existing = await _api.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName ?? $"{user.Id}@eshoponweb.invalid";
        var firstName = DeriveFirstName(email);
        var created = await _api.CreateCustomerAsync(new MaxioNewCustomer
        {
            FirstName = firstName,
            LastName = "Subscriber",
            Organization = "eShopOnWeb",
            Email = email,
            Reference = user.Id
        }, cancellationToken);

        _logger.LogInformation("Created Maxio customer {CustomerId} (reference {Reference}) for user {UserId}.",
            created.Id, created.Reference, user.Id);
        return created;
    }

    private Result<SubscriptionEnrollment> ErrorEnrollment(MaxioApiException ex, string whileDoingWhat)
    {
        _logger.LogError(ex, "Maxio Billing API error while {Activity}. Status {StatusCode}. Errors: {Errors}",
            whileDoingWhat, ex.StatusCode, string.Join("; ", ex.Errors));
        return Result<SubscriptionEnrollment>.Error(
            $"Billing system error while {whileDoingWhat}: {ex.Message}");
    }

    private Result<IEnumerable<SubscriptionPlanInfo>> ErrorPlans(MaxioApiException ex, string whileDoingWhat)
    {
        _logger.LogError(ex, "Maxio Billing API error while {Activity}. Status {StatusCode}. Errors: {Errors}",
            whileDoingWhat, ex.StatusCode, string.Join("; ", ex.Errors));
        return Result<IEnumerable<SubscriptionPlanInfo>>.Error(
            $"Billing system error while {whileDoingWhat}: {ex.Message}");
    }

    private Result<IEnumerable<SubscriptionSummary>> ErrorSummaries(MaxioApiException ex, string whileDoingWhat)
    {
        _logger.LogError(ex, "Maxio Billing API error while {Activity}. Status {StatusCode}. Errors: {Errors}",
            whileDoingWhat, ex.StatusCode, string.Join("; ", ex.Errors));
        return Result<IEnumerable<SubscriptionSummary>>.Error(
            $"Billing system error while {whileDoingWhat}: {ex.Message}");
    }

    private static SubscriptionPlanInfo MapPlan(MaxioProductDto product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        PriceCents = product.PriceInCents,
        BillingInterval = $"{product.Interval} {product.IntervalUnit}".Trim(),
        RequiresPaymentMethod = product.RequireCreditCard == true
    };

    private static SubscriptionSummary MapSubscription(MaxioSubscriptionDto subscription) => new()
    {
        SubscriptionId = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
        State = subscription.State ?? string.Empty,
        PriceCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
        NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt
    };

    private static string DeriveFirstName(string email)
    {
        var localPart = email.Split('@', 2)[0];
        return localPart.Length > 0 ? localPart : "eShop";
    }
}

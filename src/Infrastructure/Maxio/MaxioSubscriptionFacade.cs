using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements the subscription use-cases against Maxio Advanced Billing, which is the
/// billing system of record. Idempotency is anchored on a stable customer "reference"
/// derived from the eShopOnWeb user id, so the mapping survives process restarts with no
/// local persistence.
/// </summary>
public class MaxioSubscriptionFacade : ISubscriptionFacade
{
    // Namespace the application-side reference so it cannot collide with references used
    // by other integrations on the same Maxio site.
    private const string ReferencePrefix = "eshoponweb-user:";

    private readonly IMaxioBillingClient _billing;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SubscriptionIdempotencyLocks _locks;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionFacade> _logger;

    public MaxioSubscriptionFacade(
        IMaxioBillingClient billing,
        UserManager<ApplicationUser> userManager,
        SubscriptionIdempotencyLocks locks,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionFacade> logger)
    {
        _billing = billing;
        _userManager = userManager;
        _locks = locks;
        _logger = logger;
        _options = options.Value;
    }

    public string ProductFamilyHandle
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
            {
                throw new BillingConfigurationException("Maxio:ProductFamilyHandle is not configured.");
            }
            return _options.ProductFamilyHandle;
        }
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return _billing.ListPlansAsync(ProductFamilyHandle, cancellationToken);
    }

    public async Task<SubscribeOutcome> SubscribeAsync(ClaimsPrincipal caller, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required to subscribe.", nameof(planHandle));
        }

        planHandle = planHandle.Trim();

        // Validate the requested plan exists and belongs to the configured family so a
        // caller cannot enroll against an arbitrary product on the Maxio site.
        var plan = await _billing.FindPlanByHandleAsync(planHandle, cancellationToken);
        if (plan is null || !string.Equals(plan.ProductFamilyHandle, ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new PlanNotFoundException(planHandle);
        }

        var (customerId, reference) = await ResolveCallerAsync(caller);

        // Serialize concurrent submits for this user+plan, then check-then-create.
        return await _locks.RunAsync($"{reference}|{planHandle}", async () =>
        {
            var existing = (await _billing.ListSubscriptionsForCustomerAsync(customerId, cancellationToken))
                .FirstOrDefault(s => string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase)
                                      && SubscriptionState.IsLive(s.State));

            if (existing is not null)
            {
                _logger.LogInformation("User {Reference} already subscribed to plan {PlanHandle}; returning existing subscription {SubscriptionId}.",
                    reference, planHandle, existing.Id);
                return new SubscribeOutcome { Subscription = existing, AlreadySubscribed = true };
            }

            var uniquenessToken = Guid.NewGuid().ToString("N");
            try
            {
                var created = await _billing.CreateSubscriptionAsync(customerId, planHandle, uniquenessToken, cancellationToken);
                _logger.LogInformation("Created subscription {SubscriptionId} for {Reference} on plan {PlanHandle} (state {State}).",
                    created.Id, reference, planHandle, created.State);
                return new SubscribeOutcome { Subscription = created, AlreadySubscribed = false };
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 409)
            {
                // Duplicate-prevention token clash (e.g. a retried request). Read back the
                // subscription that actually landed rather than assuming the outcome.
                _logger.LogWarning(ex, "Maxio reported a duplicate submission for {Reference}/{PlanHandle}; reading back the existing subscription.",
                    reference, planHandle);

                var landed = (await _billing.ListSubscriptionsForCustomerAsync(customerId, cancellationToken))
                    .FirstOrDefault(s => string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase)
                                         && SubscriptionState.IsLive(s.State));

                if (landed is null) throw;

                return new SubscribeOutcome { Subscription = landed, AlreadySubscribed = true };
            }
        });
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetMySubscriptionsAsync(ClaimsPrincipal caller, CancellationToken cancellationToken = default)
    {
        var (customerId, _) = await ResolveCallerAsync(caller);
        return await _billing.ListSubscriptionsForCustomerAsync(customerId, cancellationToken);
    }

    /// <summary>
    /// Ensures a billing customer exists for the caller (idempotent, backed by Maxio's
    /// unique-reference enforcement) and returns its id and the reference used.
    /// </summary>
    private async Task<(long CustomerId, string Reference)> ResolveCallerAsync(ClaimsPrincipal caller)
    {
        var username = caller.Identity?.Name
            ?? caller.FindFirst(ClaimTypes.Name)?.Value
            ?? caller.FindFirst(ClaimTypes.Email)?.Value;

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new UnauthorizedAccessException("The caller's identity could not be resolved from the token.");
        }

        var user = await _userManager.FindByNameAsync(username)
            ?? throw new UnauthorizedAccessException($"User '{username}' was not found.");

        var reference = $"{ReferencePrefix}{user.Id}";
        var email = user.Email ?? username;

        var (firstName, lastName) = ResolveNames(user.UserName ?? username);

        var existing = await _billing.FindCustomerByReferenceAsync(reference);
        if (existing is not null)
        {
            return (existing.Id, reference);
        }

        try
        {
            var created = await _billing.CreateCustomerAsync(new BillingCustomerDraft
            {
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            });
            return (created.Id, reference);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Lost the race to create the same reference (Maxio enforces uniqueness).
            // Re-read and adopt the customer that now exists.
            var recovered = await _billing.FindCustomerByReferenceAsync(reference)
                ?? throw new MaxioApiException(ex.StatusCode, ex.Errors, ex.RawBody);
            _logger.LogInformation("Customer for {Reference} already existed (race); adopted id {CustomerId}.", reference, recovered.Id);
            return (recovered.Id, reference);
        }
    }

    /// <summary>
    /// Advanced Billing requires a non-blank first and last name on a customer. The sample
    /// ApplicationUser carries only a username/email, so a display name is derived from it.
    /// </summary>
    private static (string First, string Last) ResolveNames(string identityName)
    {
        var localPart = identityName.Contains('@') ? identityName.Split('@')[0] : identityName;
        localPart = string.IsNullOrWhiteSpace(localPart) ? identityName : localPart;

        var tokens = localPart.Split(new[] { ' ', '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 2)
        {
            return (tokens[0], string.Join(' ', tokens.Skip(1)));
        }

        // Single token (e.g. "demouser"): use it as the given name and a stable placeholder
        // for the surname, which the provider requires.
        return (tokens.Length == 1 ? tokens[0] : localPart, "Account");
    }
}

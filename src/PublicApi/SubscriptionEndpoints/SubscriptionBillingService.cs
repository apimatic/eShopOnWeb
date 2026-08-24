using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Orchestrates the subscription billing flows against Maxio Advanced Billing:
/// plan discovery, idempotent customer provisioning, and idempotent subscribe.
/// </summary>
public class SubscriptionBillingService
{
    // End-of-life states per the spec's Subscription-State enum; a subscription in one of these
    // states does not block subscribing again to the same plan.
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "on_hold", "suspended", "trial_ended"
    };

    private readonly MaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        MaxioClient maxioClient,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioSettings> settings,
        ILogger<SubscriptionBillingService> logger)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var products = await _maxioClient.ListProductsForProductFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null)
            .OrderBy(p => p.PriceInCents)
            .Select(p => new SubscriptionPlanDto
            {
                ProductId = p.Id,
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty,
                ProductFamilyHandle = p.ProductFamily?.Handle
            })
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(ClaimsPrincipal principal, string productHandle, string? firstName, string? lastName, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new UnknownSubscriptionPlanException("A productHandle is required.");
        }

        var user = await GetUserAsync(principal);

        // Only plans from the configured product family are subscribable through this API.
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new UnknownSubscriptionPlanException($"Unknown subscription plan '{productHandle}'.");
        }

        var customer = await EnsureCustomerAsync(user, firstName, lastName, cancellationToken);

        // Idempotency: the subscription reference ties this user to this plan, so a retried or
        // double-submitted subscribe returns the existing subscription instead of creating a second one.
        var subscriptionReference = $"{user.Id}:{plan.Handle}";
        var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Reference, subscriptionReference, StringComparison.Ordinal) &&
            !IsEndOfLife(s.State));

        if (existing is not null)
        {
            _logger.LogInformation("Subscription {SubscriptionId} already exists for reference {Reference}; returning it.", existing.Id, subscriptionReference);
            return new SubscribeResult(ToDto(existing), AlreadyExisted: true);
        }

        // "remittance" (per the spec's Collection-Method and the createSubscription "Basic" example)
        // enrolls without capturing payment, matching the no-payment-method-required plan setup.
        var created = await _maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            ProductHandle = plan.Handle,
            CustomerReference = user.Id,
            Reference = subscriptionReference,
            PaymentCollectionMethod = "remittance"
        }, cancellationToken);

        _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} on plan {PlanHandle}.", created.Id, user.Id, plan.Handle);
        return new SubscribeResult(ToDto(created), AlreadyExisted: false);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var user = await GetUserAsync(principal);
        var customer = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(ToDto)
            .ToList();
    }

    private async Task<ApplicationUser> GetUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(username))
        {
            throw new UnauthorizedAccessException("The token does not contain a username claim.");
        }

        var user = await _userManager.FindByNameAsync(username);
        return user ?? throw new UnauthorizedAccessException($"User '{username}' was not found.");
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, string? firstName, string? lastName, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName
            ?? throw new InvalidOperationException($"User '{user.Id}' has neither an email nor a username to provision a Maxio customer with.");
        var localPart = email.Split('@')[0];

        var createRequest = new MaxioCreateCustomer
        {
            FirstName = string.IsNullOrWhiteSpace(firstName) ? localPart : firstName!,
            LastName = string.IsNullOrWhiteSpace(lastName) ? "Subscriber" : lastName!,
            Email = email,
            Reference = user.Id
        };

        try
        {
            return await _maxioClient.CreateCustomerAsync(createRequest, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Customer reference is unique per site; a concurrent request may have created it first.
            var raced = await _maxioClient.FindCustomerByReferenceAsync(user.Id, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
            throw;
        }
    }

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new InvalidOperationException(
                "The Maxio integration is not configured. Provide Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl) via user-secrets or environment variables.");
        }
    }

    private static bool IsEndOfLife(string? state) => state is not null && EndOfLifeStates.Contains(state);

    private static SubscriptionDto ToDto(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        Reference = subscription.Reference,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        ProductPriceInCents = subscription.ProductPriceInCents,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        BalanceInCents = subscription.BalanceInCents,
        MaxioCustomerId = subscription.Customer?.Id ?? 0
    };
}

public record SubscribeResult(SubscriptionDto Subscription, bool AlreadyExisted);

public class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string message) : base(message)
    {
    }
}

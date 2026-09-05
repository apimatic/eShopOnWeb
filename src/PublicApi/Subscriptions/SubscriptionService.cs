using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService
{
    private static readonly HashSet<string> ActiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "pending", "awaiting_signup", "past_due", "soft_failure", "paused", "unpaid"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly CatalogContext _catalogContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SubscriptionRequestCoordinator _coordinator;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly MaxioOptions _options;

    public SubscriptionService(
        IMaxioClient maxioClient,
        CatalogContext catalogContext,
        UserManager<ApplicationUser> userManager,
        SubscriptionRequestCoordinator coordinator,
        Microsoft.Extensions.Options.IOptions<MaxioOptions> options,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _catalogContext = catalogContext;
        _userManager = userManager;
        _coordinator = coordinator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        EnsureCatalogConfigured();
        var products = await _maxioClient.GetProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products.Select(ToPlanDto).ToArray();
    }

    public async Task<SubscribeResult> SubscribeAsync(ClaimsPrincipal principal, string? requestedPlanHandle, CancellationToken cancellationToken)
    {
        var authenticatedUser = await GetUserAsync(principal);
        var user = authenticatedUser.User;
        var planHandle = requestedPlanHandle?.Trim();
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionRequestException("planHandle is required.");
        }

        EnsureCatalogConfigured();

        var userReference = CustomerReference(authenticatedUser.Identity);
        var subscriptionReference = SubscriptionReference(authenticatedUser.Identity);
        var requestToken = UniquenessToken($"subscription:{userReference}");
        var userLock = _coordinator.ForUser(userReference);
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var plans = await _maxioClient.GetProductsAsync(_options.ProductFamilyHandle, cancellationToken);
            var plan = plans.FirstOrDefault(candidate => string.Equals(candidate.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                throw new SubscriptionRequestException("The requested subscription plan is not available.");
            }

            var customer = await _maxioClient.GetCustomerByReferenceAsync(userReference, cancellationToken);
            if (customer is null)
            {
                customer = await CreateOrRecoverCustomerAsync(user, userReference, cancellationToken);
            }

            var mapping = await _catalogContext.SubscriptionMappings
                .SingleOrDefaultAsync(item => item.UserReference == userReference, cancellationToken);
            var subscriptions = await _maxioClient.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var subscription = subscriptions.FirstOrDefault(item =>
                string.Equals(item.Reference, subscriptionReference, StringComparison.OrdinalIgnoreCase));

            if (subscription is null && mapping is not null)
            {
                subscription = subscriptions.FirstOrDefault(item => item.Id == mapping.MaxioSubscriptionId);
            }

            var alreadyExists = subscription is not null;

            if (subscription is null)
            {
                var current = subscriptions.FirstOrDefault(item => ActiveStates.Contains(item.State));
                if (current is not null)
                {
                    throw new SubscriptionConflictException("This account already has an active subscription.");
                }

                subscription = await CreateOrRecoverSubscriptionAsync(
                    customer.Id,
                    plan.Handle,
                    subscriptionReference,
                    requestToken,
                    cancellationToken);
            }

            var actualPlan = subscription.Product ?? plan;
            if (!string.IsNullOrWhiteSpace(actualPlan.Handle) &&
                !string.Equals(actualPlan.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
            {
                throw new SubscriptionConflictException("This account already has a different subscription plan.");
            }

            await SaveMappingAsync(userReference, customer.Id, subscription, actualPlan, cancellationToken);
            return new SubscribeResult(ToSubscriptionDto(subscription, actualPlan), alreadyExists);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var authenticatedUser = await GetUserAsync(principal);
        var customer = await _maxioClient.GetCustomerByReferenceAsync(CustomerReference(authenticatedUser.Identity), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var plans = await _maxioClient.GetProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        var plansByHandle = plans.ToDictionary(item => item.Handle, StringComparer.OrdinalIgnoreCase);
        var result = new List<SubscriptionDto>(subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            var product = subscription.Product;
            if (product is null || string.IsNullOrWhiteSpace(product.Handle) || !plansByHandle.TryGetValue(product.Handle, out var configuredPlan))
            {
                configuredPlan = product ?? new MaxioProduct();
            }

            result.Add(ToSubscriptionDto(subscription, configuredPlan));
        }

        return result;
    }

    private async Task<(ApplicationUser User, string Identity)> GetUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new SubscriptionRequestException("The authenticated user could not be identified.");
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            throw new SubscriptionRequestException("The authenticated user no longer exists.");
        }

        return (user, username);
    }

    private async Task<MaxioCustomer> CreateOrRecoverCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var email = user.Email ?? user.UserName ?? reference;
        var firstName = email.Split('@')[0];
        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = "eShop";
        }

        try
        {
            return await _maxioClient.CreateCustomerAsync(
                firstName,
                "Shopper",
                email,
                reference,
                UniquenessToken($"customer:{reference}"),
                cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == 409 || exception.StatusCode == 422)
        {
            _logger.LogInformation("Recovering Maxio customer after a duplicate create response for reference {CustomerReference}.", reference);
            return await _maxioClient.GetCustomerByReferenceAsync(reference, cancellationToken)
                ?? throw new MaxioApiException(exception.StatusCode, "recover customer after duplicate create");
        }
    }

    private async Task<MaxioSubscription> CreateOrRecoverSubscriptionAsync(
        long customerId,
        string planHandle,
        string reference,
        string uniquenessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _maxioClient.CreateSubscriptionAsync(customerId, planHandle, reference, uniquenessToken, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == 409 || exception.StatusCode == 422)
        {
            _logger.LogInformation("Recovering Maxio subscription after a duplicate create response for reference {SubscriptionReference}.", reference);
            return await _maxioClient.GetSubscriptionByReferenceAsync(reference, cancellationToken)
                ?? throw new MaxioApiException(exception.StatusCode, "recover subscription after duplicate create");
        }
    }

    private async Task SaveMappingAsync(
        string userReference,
        long customerId,
        MaxioSubscription subscription,
        MaxioProduct product,
        CancellationToken cancellationToken)
    {
        var mapping = await _catalogContext.SubscriptionMappings
            .SingleOrDefaultAsync(item => item.UserReference == userReference, cancellationToken);
        var reference = subscription.Reference ?? $"eshop-subscription:{subscription.Id}";
        var nextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt;
        if (mapping is null)
        {
            mapping = new SubscriptionMapping(
                userReference,
                customerId,
                subscription.Id,
                reference,
                product.Handle,
                subscription.State,
                nextBillingDate);
            _catalogContext.SubscriptionMappings.Add(mapping);
        }
        else
        {
            mapping.UpdateFromMaxio(customerId, subscription.Id, reference, product.Handle, subscription.State, nextBillingDate);
        }

        await _catalogContext.SaveChangesAsync(cancellationToken);
    }

    private void EnsureCatalogConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new SubscriptionConfigurationException("Maxio:ProductFamilyHandle is required.");
        }
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description ?? string.Empty,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription, MaxioProduct product) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference ?? string.Empty,
        PlanHandle = product.Handle,
        PlanName = product.Name,
        PriceInCents = subscription.ProductPriceInCents ?? product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        State = subscription.State,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
    };

    private static string CustomerReference(string identity) => $"eshop-user:{StableIdentityHash(identity)}";

    private static string SubscriptionReference(string identity) => $"eshop-subscription:{StableIdentityHash(identity)}";

    private static string StableIdentityHash(string identity)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string UniquenessToken(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record SubscribeResult(SubscriptionDto Subscription, bool AlreadyExists);

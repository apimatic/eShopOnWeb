using System;
using System.Collections.Concurrent;
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

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly IMaxioBillingClient _maxio;
    private readonly CatalogContext _catalogContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioBillingClient maxio,
        CatalogContext catalogContext,
        UserManager<ApplicationUser> userManager,
        Microsoft.Extensions.Options.IOptions<MaxioSettings> settings,
        ILogger<SubscriptionService> logger)
    {
        _maxio = maxio;
        _catalogContext = catalogContext;
        _userManager = userManager;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await GetConfiguredProductsAsync(cancellationToken);
        return products.Select(ToPlanDto).ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new ArgumentException("PlanHandle is required.", nameof(planHandle));

        var user = await GetUserAsync(principal);
        var products = await GetConfiguredProductsAsync(cancellationToken);
        var product = products.SingleOrDefault(item => string.Equals(item.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (product is null)
            throw new ArgumentException("The requested subscription plan is not available.", nameof(planHandle));

        // The token username/email is stable across database reseeds. The local
        // mapping still relates the Maxio records to the current eShop user ID.
        var identityReference = GetIdentityReference(user);
        var customerReference = GetCustomerReference(identityReference);
        var subscriptionReference = GetSubscriptionReference(identityReference, product.Handle);
        var userLock = UserLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var mapping = await _catalogContext.SubscriptionMappings
                .SingleOrDefaultAsync(item => item.UserId == user.Id && item.PlanHandle == product.Handle, cancellationToken);

            if (mapping is not null)
            {
                var mappedSubscription = await _maxio.GetSubscriptionAsync(mapping.MaxioSubscriptionId, cancellationToken);
                if (mappedSubscription is not null)
                    return ToSubscriptionDto(mappedSubscription, product);

                _catalogContext.SubscriptionMappings.Remove(mapping);
                await _catalogContext.SaveChangesAsync(cancellationToken);
            }

            // The reference lookup makes the operation recoverable when Maxio accepted
            // the request but the local write failed or a second process is racing us.
            var existingSubscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existingSubscription is not null)
            {
                EnsureSubscriptionBelongsToCustomer(existingSubscription, null);
                var existingCustomer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
                EnsureSubscriptionBelongsToCustomer(existingSubscription, existingCustomer.Id);
                await SaveMappingAsync(user.Id, customerReference, existingCustomer.Id, subscriptionReference, existingSubscription, product.Handle, cancellationToken);
                return ToSubscriptionDto(existingSubscription, product);
            }

            var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
            var subscription = await CreateOrRecoverSubscriptionAsync(customer.Id, product.Handle, subscriptionReference, cancellationToken);
            EnsureSubscriptionBelongsToCustomer(subscription, customer.Id);
            await SaveMappingAsync(user.Id, customerReference, customer.Id, subscriptionReference, subscription, product.Handle, cancellationToken);
            return ToSubscriptionDto(subscription, product);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var customer = await _maxio.FindCustomerByReferenceAsync(GetCustomerReference(GetIdentityReference(user)), cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDto>();

        var products = await GetConfiguredProductsAsync(cancellationToken);
        var productsByHandle = products.ToDictionary(item => item.Handle, StringComparer.OrdinalIgnoreCase);
        var subscriptions = await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .Select(subscription =>
            {
                var handle = subscription.Product?.Handle ?? string.Empty;
                return productsByHandle.TryGetValue(handle, out var product)
                    ? ToSubscriptionDto(subscription, product)
                    : null;
            })
            .Where(subscription => subscription is not null)
            .Select(subscription => subscription!)
            .ToList();
    }

    private async Task<ApplicationUser> GetUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
            throw new UnauthorizedAccessException("The bearer token does not identify a user.");

        var user = await _userManager.FindByNameAsync(username);
        return user ?? throw new UnauthorizedAccessException("The authenticated eShop user was not found.");
    }

    private async Task<IReadOnlyList<MaxioProduct>> GetConfiguredProductsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");

        return await _maxio.GetProductsAsync(_settings.ProductFamilyHandle, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
            return existing;

        var email = user.Email ?? user.UserName ?? reference;
        var name = email.Split('@', 2)[0];
        if (string.IsNullOrWhiteSpace(name))
            name = "eShop shopper";

        try
        {
            return await _maxio.CreateCustomerAsync(name, "Customer", email, reference, cancellationToken);
        }
        catch (MaxioApiException ex) when ((int)ex.StatusCode is 400 or 409 or 422)
        {
            // Maxio enforces unique customer references. If another request won
            // the create race, recover the customer rather than creating another.
            var racedCustomer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (racedCustomer is not null)
                return racedCustomer;

            _logger.LogError(ex, "Maxio rejected creation of customer for eShop user {UserId}.", user.Id);
            throw;
        }
    }

    private async Task<MaxioSubscription> CreateOrRecoverSubscriptionAsync(long customerId, string planHandle, string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await _maxio.CreateSubscriptionAsync(customerId, planHandle, reference, cancellationToken);
        }
        catch (MaxioApiException ex) when ((int)ex.StatusCode is 400 or 409 or 422)
        {
            var racedSubscription = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (racedSubscription is not null)
                return racedSubscription;

            _logger.LogError(ex, "Maxio rejected creation of subscription with reference {SubscriptionReference}.", reference);
            throw;
        }
    }

    private async Task SaveMappingAsync(
        string userId,
        string customerReference,
        long customerId,
        string subscriptionReference,
        MaxioSubscription subscription,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var mapping = await _catalogContext.SubscriptionMappings
            .SingleOrDefaultAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);

        if (mapping is null)
        {
            mapping = new SubscriptionMapping
            {
                UserId = userId,
                CustomerReference = customerReference,
                MaxioCustomerId = customerId,
                SubscriptionReference = subscriptionReference,
                MaxioSubscriptionId = subscription.Id,
                PlanHandle = planHandle,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            await _catalogContext.SubscriptionMappings.AddAsync(mapping, cancellationToken);
        }
        else
        {
            mapping.MaxioCustomerId = customerId;
            mapping.MaxioSubscriptionId = subscription.Id;
            mapping.SubscriptionReference = subscriptionReference;
        }

        await _catalogContext.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureSubscriptionBelongsToCustomer(MaxioSubscription subscription, long? customerId)
    {
        var actualCustomerId = subscription.CustomerId ?? subscription.Customer?.Id;
        if (customerId.HasValue && actualCustomerId.HasValue && actualCustomerId != customerId)
            throw new InvalidOperationException("The Maxio subscription is associated with a different customer.");
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        PaymentMethodRequired = product.RequireCreditCard ?? false
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription, MaxioProduct product) => new()
    {
        Id = subscription.Id,
        CustomerId = subscription.CustomerId ?? subscription.Customer?.Id ?? 0,
        PlanHandle = subscription.Product?.Handle ?? product.Handle,
        PlanName = subscription.Product?.Name ?? product.Name,
        Price = (subscription.ProductPriceInCents == 0 ? product.PriceInCents : subscription.ProductPriceInCents) / 100m,
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt,
        CurrentBillingAmountInCents = subscription.CurrentBillingAmountInCents
    };

    private static string GetIdentityReference(ApplicationUser user) =>
        (user.Email ?? user.UserName ?? user.Id).Trim().ToUpperInvariant();

    private static string GetCustomerReference(string identityReference) =>
        $"eshop-user:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identityReference))).ToLowerInvariant()}";

    private static string GetSubscriptionReference(string identityReference, string planHandle) =>
        $"eshop-subscription:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{identityReference}:{planHandle.ToUpperInvariant()}"))).ToLowerInvariant()}";
}
